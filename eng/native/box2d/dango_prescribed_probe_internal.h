#ifndef DANGO_PRESCRIBED_PROBE_INTERNAL_H
#define DANGO_PRESCRIBED_PROBE_INTERNAL_H
#ifndef DANGO_PRESCRIBED_CONTRACT_TESTS
#error Prescribed probes are isolated executables only.
#endif
#include <stdio.h>
static const dango_self_physics_config prescribed_probe_config={
    .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
    .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=5.2f,.perimeter_hertz=20.1f,
    .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.ground_deformation_strength=3.5f,
    .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,.core_exclusion_ratio=.25f,
    .angle_strength=10,.local_angle_balance_strength=1.7f,.top_inward_force=.5f,.gravity=9.8f,.core_mass=.12f,.node_mass=.015f,
    .drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,.contact_hertz=28,
    .linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
    .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,
    .jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f
};
static int prescribed_probe_checks,prescribed_probe_failures;
static void prescribed_probe_check(bool pass,const char* name)
{
    ++prescribed_probe_checks;
    if (!pass) { ++prescribed_probe_failures; fprintf(stderr,"FAIL prescribed %s\n",name); }
}
static collision_review* prescribed_probe_world(int count,int* ids)
{
    void* handle=NULL;
    if (dango_physics_world_create(&prescribed_probe_config,2,&handle)) return NULL;
    for (int b=0;b<count;++b) if (dango_physics_world_add(handle,b ? 10.0f : -10.0f,0,&ids[b]))
    { dango_physics_world_destroy(handle); return NULL; }
    if (dango_physics_world_bind_prescribed_epoch(handle,7)) { dango_physics_world_destroy(handle); return NULL; }
    return handle;
}
static dango_prescribed_step prescribed_probe_input(int count,const int* ids,float x0,float x1,float y)
{
    dango_prescribed_step input={.abi_version=1,.frame_size=248,.epoch=7,.ordinal=1,.start_time=0,.end_time=1,
        .start_revision=1,.end_revision=1,.body_count=count};
    for (int b=0;b<count;++b)
        input.bodies[b]=(dango_prescribed_body){.entity_id=ids[b],.start_x=b ? x1 : x0,.end_x=b ? x1 : x0,
            .start_y=y,.end_y=y};
    return input;
}
static int prescribed_probe_step(collision_review* p,const dango_prescribed_step* input,dango_prescribed_trace* trace,float* state)
{
    int ids[2]={0},count=0,contacts=0;
    return dango_physics_world_step_prescribed(p,input,248,ids,state,84,&count,&contacts,trace,3936);
}
static dango_prescribed_step prescribed_probe_next(dango_prescribed_step input)
{
    ++input.ordinal; input.start_time=input.end_time; ++input.end_time;
    input.start_revision=input.end_revision; input.start_pair_count=input.end_pair_count;
    input.start_pairs[0]=input.end_pairs[0];
    for (int b=0;b<input.body_count;++b)
    {
        dango_prescribed_body* body=&input.bodies[b];
        body->start_floor=body->end_floor; body->start_support=body->end_support;
        body->start_x=body->end_x; body->start_y=body->end_y; body->start_vx=body->end_vx; body->start_vy=body->end_vy;
    }
    return input;
}
static void prescribed_probe_rejection(collision_review* p,const dango_prescribed_step* input,int expected,const char* name)
{
    float safe[126],out[84]; memcpy(safe,p->snapshot,sizeof safe); memset(out,0x55,sizeof out);
    dango_prescribed_trace trace; memset(&trace,0x55,sizeof trace);
    dango_prescribed_trace untouched=trace; float output_before[84]; memcpy(output_before,out,sizeof out);
    b2Vec2 core[2]; int ordinal=(int)p->physics_step;
    for (int s=0;s<2;++s) core[s]=p->active[s] ? b2Body_GetPosition(p->bodies[s]->core_body_id) : b2Vec2_zero;
    prescribed_probe_check(prescribed_probe_step(p,input,&trace,out)==expected,name);
    prescribed_probe_check(memcmp(safe,p->snapshot,sizeof safe)==0 && (int)p->physics_step==ordinal &&
        memcmp(&untouched,&trace,sizeof trace)==0 && memcmp(output_before,out,sizeof out)==0,"invalid group does not publish or write output");
    for (int s=0;s<2;++s) if (p->active[s])
        prescribed_probe_check(b2Distance(core[s],b2Body_GetPosition(p->bodies[s]->core_body_id))==0,"invalid group preserves actual core");
}
static void prescribed_probe_identity_and_modes(void)
{
    int ids[2]={0}; collision_review* p=prescribed_probe_world(2,ids);
    prescribed_probe_check(p!=NULL,"create validation world"); if (!p) return;
    dango_prescribed_step good=prescribed_probe_input(2,ids,-2,2,3),bad=good;
    bad.epoch=8; prescribed_probe_rejection(p,&bad,-421,"foreign bound epoch");
    bad=good; bad.start_revision=0; prescribed_probe_rejection(p,&bad,-410,"zero true revision");
    bad=good; bad.ordinal=0; prescribed_probe_rejection(p,&bad,-410,"zero ordinal");
    bad=good; bad.ordinal=2; prescribed_probe_rejection(p,&bad,-410,"skipped ordinal");
    bad=good; bad.bodies[1].entity_id=ids[0]; prescribed_probe_rejection(p,&bad,-412,"duplicate identity");
    bad=good; bad.bodies[1].end_x=NAN; prescribed_probe_rejection(p,&bad,-410,"nonfinite entire group");
    bad=good; bad.start_time=NAN; prescribed_probe_rejection(p,&bad,-410,"nonfinite sample time");
    bad=good; bad.end_time=2; prescribed_probe_rejection(p,&bad,-410,"wrong common duration");
    bad=good; bad.bodies[0].start_floor=1; prescribed_probe_rejection(p,&bad,-410,"new body start semantics empty");
    bad=good; bad.bodies[0].start_x-=.1f; prescribed_probe_rejection(p,&bad,-410,"new body start velocity backprojection");
    bad=good; bad.end_pair_count=1; bad.end_pairs[0]=(dango_authority_pair){.entity_a=ids[0],.entity_b=ids[1],.flags=1,
        .nx=-1,.gap=0,.minimum_projected_width=1}; prescribed_probe_rejection(p,&bad,-410,"reversed normal");
    bad.end_pairs[0].nx=1; prescribed_probe_rejection(p,&bad,-410,"unreachable distant pair");
    bad=good; bad.end_pair_count=1; bad.end_pairs[0]=(dango_authority_pair){.entity_a=ids[0],.entity_b=ids[1],.flags=3,
        .nx=1,.gap=0,.minimum_projected_width=1}; bad.bodies[0].end_support=ids[1];
    prescribed_probe_rejection(p,&bad,-410,"side contact is not support");
    float state[84]={0},controls[12]={0}; int count=0,contacts=0,error=0,out_ids[2]={0};
    prescribed_probe_check(dango_physics_world_step(p,prescribed_dt(),ids,controls,2,out_ids,state,84,&count,&contacts,&error)==-421,
        "neutral ordinary step cannot bypass prescribed mode");
    dango_presentation_target targets[2]={{ids[0],0,3,0,0},{ids[1],2,3,0,0}};
    prescribed_probe_check(dango_physics_world_apply_presentation_targets(p,targets,2,sizeof targets[0])==-421,"legacy targets rejected");
    prescribed_probe_check(dango_physics_world_enable_authority(p)==-421,"authority role upgrade rejected");
    prescribed_probe_check(dango_physics_world_reset(p)==-421,"reset cannot remove prescribed fault/identity history");
    prescribed_probe_check(dango_physics_world_translate_horizontal(p,0)==-421,"translate cannot alter prescribed carriers");
    prescribed_probe_check(dango_physics_world_set_horizontal_bounds(p,-20,20)==-421 &&
        dango_physics_world_clear_horizontal_bounds(p)==-421,"boundary configuration frozen on bind");
    prescribed_probe_check(dango_physics_world_set_static_platform(p,0,1,2,.3f,.72f)==-421 &&
        dango_physics_world_clear_static_platform(p)==-421 && dango_physics_world_remove_round_ball(p,1)==-421,"old furniture entrances reject");
    dango_furniture_token token={0};
    prescribed_probe_check(dango_physics_world_cancel_prepared_furniture_change(p,&token)==-421 &&
        dango_physics_world_cancel_prepared_static_platform_change(p,&token)==-421,"old furniture cancel entrances reject");
    dango_collision_test_interaction_flags(p,255); dango_collision_disable_for_test(p); dango_collision_disable_damping_for_test(p);
    prescribed_probe_check(p->legacy_interaction_flags==0 && p->disable_contact_for_test==0 && p->disable_damping_for_test==0,"debug controls cannot downgrade mode");
    dango_prescribed_trace trace={0};
    int call=prescribed_probe_step(p,&good,&trace,state);
    prescribed_probe_check(call==0 && trace.error_code==0,"first full identity step");
    if (call==0 && !trace.error_code)
    {
        good=prescribed_probe_next(good); bad=good; bad.bodies[0].start_floor=1;
        prescribed_probe_rejection(p,&bad,-410,"start floor continuity");
        bad=good; bad.start_revision=2; prescribed_probe_rejection(p,&bad,-410,"start revision continuity");
        bad=good; bad.bodies[0].start_support=ids[1]; prescribed_probe_rejection(p,&bad,-410,"start support continuity");
        p->prescribed_previous.ordinal=UINT64_MAX; bad=good; bad.ordinal=0;
        prescribed_probe_rejection(p,&bad,-410,"ordinal overflow cannot wrap");
        p->prescribed_previous.ordinal=1;
        b2BodyId surviving=p->bodies[1]->core_body_id;
        prescribed_probe_check(dango_physics_world_remove(p,ids[0])==0,"remove at fixed boundary");
        good.body_count=1; good.bodies[0]=good.bodies[1]; good.bodies[1]=(dango_prescribed_body){0};
        prescribed_probe_check(prescribed_probe_step(p,&good,&trace,state)==0 && !trace.error_code &&
            B2_ID_EQUALS(surviving,p->bodies[1]->core_body_id),"surviving core identity persists after removal");
    }
    dango_physics_world_destroy(p);
}
static void prescribed_probe_curves(int curve)
{
    int ids[2]={0}; collision_review* p=prescribed_probe_world(1,ids);
    prescribed_probe_check(p!=NULL,"create prescribed curve world"); if (!p) return;
    dango_prescribed_step input=prescribed_probe_input(1,ids,0,0,3); float maximum=0; int completed=0;
    if (curve) { input.bodies[0].start_vx=.2f; input.bodies[0].end_vx=.2f; input.bodies[0].start_x=-.2f*prescribed_dt(); }
    for (int tick=0;tick<120;++tick)
    {
        if (tick)
        {
            input=prescribed_probe_next(input);
            float t=(tick+1)*prescribed_dt();
            input.bodies[0].end_x=curve ? .2f*t : 0;
            if (curve==2) { input.bodies[0].end_x+=.02f*sinf(t); input.bodies[0].end_vx=.2f+.02f*cosf(t); }
        }
        float states[84]={0}; dango_prescribed_trace trace={0}; int call=prescribed_probe_step(p,&input,&trace,states);
        prescribed_probe_check(call==0 && trace.error_code==0,"actual curve step stays healthy");
        if (call || trace.error_code)
        {
            printf("{\"Suite\":\"curve-failure\",\"Variant\":%d,\"Curve\":%d,\"Ordinal\":%llu,\"Error\":%d,\"RealSubsteps\":%d,\"MaximumCoreError\":%.9g}\n",
                DANGO_PRESCRIBED_TEST_VARIANT,curve,(unsigned long long)trace.ordinal,trace.error_code,trace.substep_count,(double)trace.maximum_core_errors[0]);
            break;
        }
        prescribed_probe_check(trace.substep_count==16 && trace.sample_count==17 && trace.epoch==7 && trace.ordinal==input.ordinal,
            "exact managed dt has sixteen real substeps");
        prescribed_probe_check(trace.samples[0][0].fraction==0 && trace.samples[0][16].fraction==1,"initial and all actual substeps traced");
        maximum=fmaxf(maximum,trace.maximum_core_errors[0]);
        for (int s=0;s<17;++s)
        {
            double u=(double)s/16; b2Vec2 target,velocity; prescribed_curve(&input.bodies[0],u,&target,&velocity);
            const dango_prescribed_sample* sample=&trace.samples[0][s];
            prescribed_probe_check(fabsf(sample->actual_x-target.x)<=1e-5f && fabsf(sample->actual_y-target.y)<=1e-5f,
                "actual carrier stays on curve throughout solve");
        }
        for (int s=17;s<97;++s)
        { dango_prescribed_sample zero={0}; prescribed_probe_check(memcmp(&trace.samples[0][s],&zero,sizeof zero)==0,"unused diagnostic samples zero"); }
        ++completed;
    }
    printf("{\"Suite\":\"curve\",\"Variant\":%d,\"Curve\":%d,\"Completed\":%d,\"MaximumCoreError\":%.9g}\n",DANGO_PRESCRIBED_TEST_VARIANT,curve,completed,(double)maximum);
    dango_physics_world_destroy(p);
}
static void prescribed_probe_contact(bool impossible)
{
    int ids[2]={0}; collision_review* p=prescribed_probe_world(2,ids);
    prescribed_probe_check(p!=NULL,"create contact world"); if (!p) return;
    dango_prescribed_step input=prescribed_probe_input(2,ids,impossible ? 0 : -.55f,impossible ? .01f : .55f,3);
    input.end_pair_count=1; input.end_pairs[0]=(dango_authority_pair){.entity_a=ids[0],.entity_b=ids[1],.flags=1,.nx=1,
        .gap=-.1f,.minimum_projected_width=1.2f};
    if (!impossible) for (int b=0;b<2;++b)
    {
        input.bodies[b].start_vx=input.bodies[b].end_vx=b ? -.1f : .1f;
        input.bodies[b].start_x=input.bodies[b].end_x-input.bodies[b].end_vx*prescribed_dt();
    }
    float safe[126],states[84]={0}; memcpy(safe,p->snapshot,sizeof safe); dango_prescribed_trace trace={0};
    int call=prescribed_probe_step(p,&input,&trace,states);
    prescribed_probe_check(call==0,"contact input valid through ordinary ABI");
    if (impossible)
    {
        prescribed_probe_check(trace.error_code!=0 && memcmp(safe,p->snapshot,sizeof safe)==0,"impossible compression fails and retains last legal state");
        prescribed_probe_check(trace.ordinal==input.ordinal && trace.substep_count>0 && trace.sample_count==trace.substep_count+1,
            "runtime failure returns actual failing trace");
        int new_id=0;
        prescribed_probe_check(dango_physics_world_add(p,5,0,&new_id)!=0 && dango_physics_world_remove(p,ids[0])!=0,"fault blocks topology mutation");
        dango_prescribed_trace latched={0};
        prescribed_probe_check(prescribed_probe_step(p,&input,&latched,states)==0 && memcmp(&trace,&latched,sizeof trace)==0,"failure trace remains latched");
        dango_prescribed_step foreign=input; foreign.epoch=8;
        prescribed_probe_rejection(p,&foreign,-421,"foreign epoch rejected even after runtime fault");
        prescribed_probe_check(p->contact_steps==0,"published contact count remains last legal on failed solve");
    }
    else
    {
        prescribed_probe_check(trace.error_code==0,"feasible contact projection healthy without extra energy");
        prescribed_probe_check(p->prescribed_energy_checks>0,"actual node contact energy checked");
        prescribed_probe_check(p->prescribed_max_abs_work>0,"contact exercises actual external prescribed work");
        prescribed_probe_check(p->prescribed_max_energy_excess<=0,"actual float energy obeys frozen analytical bound");
    }
    printf("{\"Suite\":\"contact\",\"Variant\":%d,\"Impossible\":%s,\"Error\":%d,\"RealSubsteps\":%d,\"EnergyChecks\":%llu,\"Work\":%.17g,\"Residual\":%.17g,\"R32\":%.17g,\"R64\":%.17g,\"MaximumExcess\":%.17g,\"TotalWork\":%.17g,\"MaximumAbsoluteWork\":%.17g}\n",
        DANGO_PRESCRIBED_TEST_VARIANT,impossible ? "true" : "false",trace.error_code,trace.substep_count,
        (unsigned long long)p->prescribed_energy_checks,p->prescribed_last_work,p->prescribed_last_residual,
        p->prescribed_last_r32,p->prescribed_last_r64,p->prescribed_max_energy_excess,p->prescribed_total_work,p->prescribed_max_abs_work);
    dango_physics_world_destroy(p);
}
int dango_prescribed_native_tests(void)
{
    int bytes=b2GetByteCount();
    prescribed_probe_check(dango_physics_get_prescribed_step_size()==248 && dango_physics_get_prescribed_trace_size()==3936,"exact additive ABI sizes");
    uint32_t dt_bits=0; float dt=prescribed_dt(); memcpy(&dt_bits,&dt,sizeof dt_bits);
    prescribed_probe_check(dt_bits==UINT32_C(0x3C888865),"frozen runtime dt bits");
    prescribed_probe_identity_and_modes();
    for (int curve=0;curve<3;++curve) prescribed_probe_curves(curve);
    prescribed_probe_contact(false); prescribed_probe_contact(true);
    prescribed_probe_check(b2GetByteCount()==bytes,"all native allocations released");
    printf("{\"Suite\":\"prescribed-overall\",\"Variant\":%d,\"Checks\":%d,\"Failures\":%d,\"DtBits\":%u,\"AllocationDelta\":%d}\n",
        DANGO_PRESCRIBED_TEST_VARIANT,prescribed_probe_checks,prescribed_probe_failures,dt_bits,b2GetByteCount()-bytes);
    return prescribed_probe_failures;
}
#endif

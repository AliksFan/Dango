#ifndef DANGO_AUTHORITY_PROBE_INTERNAL_H
#define DANGO_AUTHORITY_PROBE_INTERNAL_H
#ifndef DANGO_AUTHORITY_CONTRACT_TESTS
#error Authority probes are only available in an isolated executable.
#endif
#include <stdio.h>

static const dango_self_physics_config authority_test_config={
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
static int authority_test_checks,authority_test_failures;
static const float authority_test_dt=.0166667f;
static void authority_check(bool pass,const char* name)
{
    ++authority_test_checks;
    if (!pass) { ++authority_test_failures; fprintf(stderr,"FAIL authority %s\n",name); }
}
static int authority_test_step(collision_review* p,const float* controls)
{
    int ids[3]={0},out_ids[3]={0},count=0,contacts=0,error=0;
    float compact[18]={0},states[126]={0};
    for (int s=0;s<p->capacity;++s) if (p->active[s])
    {
        ids[count]=p->entity_ids[s];
        memcpy(compact+count*6,controls+s*6,6*sizeof(float)); ++count;
    }
    int code=dango_physics_world_step(p,authority_test_dt,ids,compact,count,out_ids,states,126,&count,&contacts,&error);
    return code ? code : error;
}
static collision_review* authority_test_create(int count,int layout,bool enabled)
{
    void* handle=NULL;
    int code=dango_physics_world_create(&authority_test_config,2,&handle);
    if (code) return NULL;
    collision_review* p=handle;
    if (enabled && dango_physics_world_enable_authority(p)!=0) { dango_physics_world_destroy(p); return NULL; }
    for (int i=0;i<count;++i)
    {
        int id=0;
        float x=i==0 ? 0 : layout==1 ? 0 : 2.8f,y=i==1 && layout==1 ? 1.65f : 0;
        if (dango_physics_world_add(p,x,y,&id)!=0) { dango_physics_world_destroy(p); return NULL; }
    }
    return p;
}

static void authority_test_publication_fault(void)
{
    /* This injection exists only in the executable. It fails semantic output,
       leaving the legacy solver error clear and the last common frame intact. */
    for (int count=0;count<=1;++count) for (int action=0;action<11;++action)
    {
        collision_review* p=authority_test_create(count,0,true);
        authority_check(p!=NULL,"publication-fault-world-create");
        if (!p) continue;
        authority_check(dango_physics_world_set_horizontal_bounds(p,-13.2f,13.2f)==0,"publication-fault-initial-bounds");
        float controls[18]={0},states[126]={0}; int contacts=0;
        authority_check(authority_test_step(p,controls)==0,"publication-fault-initial-step");
        dango_authority_frame before={0},frame={0};
        dango_physics_world_get_authority_frame(p,&before,sizeof before);
        p->test_authority_publication_fault=true;
        authority_publish(p,false);
        p->test_authority_publication_fault=false;
        authority_check(p->error==0 && p->authority_error==DANGO_AUTHORITY_INVALID_OUTPUT,"semantic-output-fault-latched-separately");
        authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 &&
            frame.error_code==DANGO_AUTHORITY_INVALID_OUTPUT,"semantic-output-fault-visible");
        frame.error_code=before.error_code;
        authority_check(memcmp(&before,&frame,sizeof frame)==0,"semantic-output-fault-retains-last-common-frame");
        collision_review previous; memcpy(&previous,p,sizeof previous);
        dango_self_soft_body body={0}; b2Vec2 positions[17]={{0}},velocities[17]={{0}};
        if (count)
        {
            memcpy(&body,p->bodies[0],sizeof body);
            for (int n=0;n<17;++n)
            {
                positions[n]=b2Body_GetPosition(display_id(p->bodies[0],n));
                velocities[n]=b2Body_GetLinearVelocity(display_id(p->bodies[0],n));
            }
        }
        b2Vec2 ground=b2Body_GetPosition(p->bodies[0]->ground_body_id);
        b2Polygon walls[2]={b2Shape_GetPolygon(p->horizontal_walls[0]),b2Shape_GetPolygon(p->horizontal_walls[1])};
        int code=0,id=0x1234;
        switch (action)
        {
            case 0: code=dango_collision_step(p,authority_test_dt,controls,states,126,&contacts); break;
            case 1: code=authority_test_step(p,controls); break;
            case 2: code=dango_physics_world_add(p,2.8f,0,&id); break;
            case 3: code=dango_physics_world_remove(p,count ? p->entity_ids[0] : 1); break;
            case 4: code=dango_physics_world_set_horizontal_bounds(p,-20,20); break;
            case 5: code=dango_physics_world_clear_horizontal_bounds(p); break;
            case 6: code=dango_physics_world_translate_horizontal(p,.25f); break;
            case 7: dango_collision_test_interaction_flags(p,8); code=p->authority_error; break;
            case 8: dango_collision_disable_damping_for_test(p); code=p->authority_error; break;
            case 9: dango_collision_disable_for_test(p); code=p->authority_error; break;
            case 10: code=dango_collision_contract_tests(p,0); break;
        }
        authority_check(code==DANGO_AUTHORITY_INVALID_OUTPUT ||
            (action==10 && code==DANGO_AUTHORITY_MODE_CONFLICT),"semantic-fault-operations-stop-with-error");
        authority_check(memcmp(&previous,p,sizeof previous)==0 && id==0x1234,"semantic-fault-no-world-metadata-or-member-mutation");
        authority_check(b2Distance(ground,b2Body_GetPosition(p->bodies[0]->ground_body_id))==0,"semantic-fault-no-floor-translation");
        for (int wall=0;wall<2;++wall)
        {
            bool valid=b2Shape_IsValid(p->horizontal_walls[wall]);
            authority_check(valid,"semantic-fault-no-wall-removal");
            if (valid)
            {
                b2Polygon after=b2Shape_GetPolygon(p->horizontal_walls[wall]);
                authority_check(memcmp(&after,&walls[wall],sizeof after)==0,"semantic-fault-no-wall-geometry-mutation");
            }
        }
        if (count)
        {
            authority_check(p->active[0] && memcmp(&body,p->bodies[0],sizeof body)==0,"semantic-fault-no-body-semantics-mutation");
            for (int n=0;n<17;++n)
            {
                b2BodyId body_id=display_id(p->bodies[0],n);
                bool valid=p->active[0] && b2Body_IsValid(body_id);
                authority_check(valid && b2Distance(positions[n],b2Body_GetPosition(body_id))==0 &&
                    b2Distance(velocities[n],b2Body_GetLinearVelocity(body_id))==0,"semantic-fault-no-live-node-mutation");
            }
        }
        authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 &&
            frame.error_code==DANGO_AUTHORITY_INVALID_OUTPUT,"semantic-fault-stays-latched");
        frame.error_code=before.error_code;
        authority_check(memcmp(&frame,&before,sizeof frame)==0,"semantic-fault-operations-retain-last-safe-common-frame");
        dango_physics_world_destroy(p);
    }
    collision_review* p=authority_test_create(1,0,true);
    if (!p) { authority_check(false,"semantic-fault-reset-world-create"); return; }
    p->test_authority_publication_fault=true; authority_publish(p,false); p->test_authority_publication_fault=false;
    authority_check(dango_physics_world_reset(p)==0 && p->authority_error==0,"explicit-reset-may-recover-semantic-fault");
    float controls[18]={0};
    authority_check(authority_test_step(p,controls)==0,"explicit-reset-recovered-world-steps");
    dango_physics_world_destroy(p);
}

static void authority_test_debug_role_guards(void)
{
    for (int presentation=0;presentation<2;++presentation) for (int action=0;action<4;++action)
    {
        collision_review* p=authority_test_create(1,0,!presentation);
        authority_check(p!=NULL,"debug-role-world-create");
        if (!p) continue;
        if (presentation)
        {
            dango_presentation_target target={.entity_id=p->entity_ids[0],.core_x=0,.core_y=.42f};
            authority_check(dango_physics_world_apply_presentation_targets(p,&target,1,sizeof target)==0,"debug-presentation-role-create");
        }
        collision_review previous; memcpy(&previous,p,sizeof previous);
        int code=0;
        if (action==0) dango_collision_test_interaction_flags(p,8);
        if (action==1) dango_collision_disable_damping_for_test(p);
        if (action==2) dango_collision_disable_for_test(p);
        if (action==3) code=dango_collision_contract_tests(p,0);
        authority_check(action!=3 || code==DANGO_AUTHORITY_MODE_CONFLICT,"debug-oracle-rejects-authority-and-presentation");
        authority_check(memcmp(&previous,p,sizeof previous)==0,"debug-export-cannot-degrade-new-roles");
        dango_physics_world_destroy(p);
    }
    for (int action=0;action<3;++action)
    {
        collision_review* p=authority_test_create(1,0,false);
        if (!p) { authority_check(false,"degraded-world-create"); continue; }
        if (action==0) dango_collision_test_interaction_flags(p,8);
        if (action==1) dango_collision_disable_damping_for_test(p);
        if (action==2) dango_collision_disable_for_test(p);
        authority_check(dango_physics_world_enable_authority(p)==DANGO_AUTHORITY_UNSUPPORTED,"degraded-world-cannot-enable-authority");
        dango_presentation_target target={.entity_id=p->entity_ids[0],.core_x=0,.core_y=.42f};
        authority_check(dango_physics_world_apply_presentation_targets(p,&target,1,sizeof target)==DANGO_AUTHORITY_UNSUPPORTED,
            "degraded-world-cannot-enable-presentation");
        dango_physics_world_destroy(p);
    }
}

int dango_authority_native_tests(void)
{
    int initial_bytes=b2GetByteCount();
    collision_review* oracle=authority_test_create(2,1,false);
    authority_check(oracle!=NULL,"legacy-oracle-world-create");
    if (!oracle) return 1;
    authority_check(dango_collision_contract_tests(oracle,0)==0,"retained-impulse-and-contact-oracles");
    dango_physics_world_destroy(oracle);
    collision_review* p=authority_test_create(2,1,true);
    authority_check(p!=NULL,"world-create");
    if (!p) return 1;
    authority_check(dango_physics_world_reset(p)==0,"reset-after-oracle");
    dango_authority_frame before={0},frame={0};
    authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 && frame.tick==0 && frame.body_count==2,"initial-frame");
    memset(&before,0x5a,sizeof before); frame=before;
    authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame-1)==-410 && memcmp(&frame,&before,sizeof frame)==0,"exact-size-before-output");
    int support_samples=0,velocity_samples=0,applied_support=0,rejected_air=0;
    float controls[18]={0};
    for (int tick=0;tick<300;++tick)
    {
        controls[8]=(tick==70 || tick==72) ? 1.0f : 0.0f;
        authority_check(authority_test_step(p,controls)==0,"step-success");
        authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 && frame.error_code==0 && frame.tick==(uint64_t)tick+1,"one-success-tick");
        for (int b=0;b<frame.body_count;++b)
        {
            int slot=-1;
            for (int s=0;s<2;++s) if (p->entity_ids[s]==frame.bodies[b].entity_id) slot=s;
            authority_check(slot>=0,"identity-map");
            if (slot<0) continue;
            b2Vec2 velocity=b2Body_GetLinearVelocity(p->bodies[slot]->core_body_id);
            authority_check(frame.bodies[b].core_vx==velocity.x && frame.bodies[b].core_vy==velocity.y,"actual-core-velocity");
            if (b2Length(velocity)>.01f) ++velocity_samples;
            int support=collision_support_slot(p,slot);
            authority_check(frame.bodies[b].support_entity_id==(support<0 ? 0 : p->entity_ids[support]),"actual-support-identity");
            if (support>=0) ++support_samples;
            if (frame.bodies[b].jump_result==DANGO_AUTHORITY_JUMP_APPLIED_SUPPORT) ++applied_support;
            if (frame.bodies[b].jump_result==DANGO_AUTHORITY_JUMP_REJECTED_AIRBORNE) ++rejected_air;
        }
        before=frame;
        authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 && memcmp(&before,&frame,sizeof frame)==0,"repeat-read-stable");
    }
    authority_check(support_samples>0 && velocity_samples>0,"real-support-and-motion-exercised");
    authority_check(applied_support==1 && rejected_air>=1,"actual-supported-jump-and-repeat-rejection");
    authority_check(dango_physics_world_reset(p)==0,"explicit-reset");
    for (int tick=0;tick<60;++tick) authority_check(authority_test_step(p,controls)==0,"restack");
    authority_check(dango_physics_world_remove(p,p->entity_ids[0])==0,"remove-lower");
    authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 && frame.body_count==1 &&
        frame.bodies[0].support_entity_id==0,"remove-clears-support-immediately");
    before=frame;
    unsigned char checkpoint[8]={0x12,0x34}; int written=99;
    authority_check(dango_physics_world_export_checkpoint(p,checkpoint,sizeof checkpoint,&written)==DANGO_AUTHORITY_UNSUPPORTED &&
        written==0 && checkpoint[0]==0x12 && checkpoint[1]==0x34,"checkpoint-export-unsupported-no-write");
    authority_check(dango_physics_world_import_checkpoint(p,checkpoint,sizeof checkpoint)==DANGO_AUTHORITY_UNSUPPORTED,"checkpoint-import-unsupported");
    authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 && memcmp(&before,&frame,sizeof frame)==0,"unsupported-import-retains-world");
    /* Fault injection is executable-only: poison live pose after publication. */
    p->error=-91;
    b2Body_SetTransform(p->bodies[1]->core_body_id,(b2Vec2){99,99},b2Rot_identity);
    authority_check(dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==0 && frame.error_code==-91,"fault-visible");
    frame.error_code=before.error_code;
    authority_check(memcmp(&before,&frame,sizeof frame)==0,"fault-preserves-entire-safe-frame");
    authority_check(authority_test_step(p,controls)==-91,"fault-latched");
    authority_check(dango_physics_world_destroy(p)==0,"destroy");
    p=authority_test_create(2,0,false);
    authority_check(p!=NULL,"presentation-world-create");
    if (p)
    {
        b2Vec2 old_positions[2][17],old_velocities[2][17];
        for (int b=0;b<2;++b) for (int n=0;n<17;++n)
        {
            old_positions[b][n]=b2Body_GetPosition(display_id(p->bodies[b],n));
            old_velocities[b][n]=b2Body_GetLinearVelocity(display_id(p->bodies[b],n));
        }
        dango_presentation_target targets[2]={
            {.entity_id=p->entity_ids[0],.core_x=1,.core_y=.6f,.core_vx=.5f,.core_vy=0},
            {.entity_id=p->entity_ids[1],.core_x=NAN,.core_y=.6f,.core_vx=0,.core_vy=0}};
        authority_check(dango_physics_world_apply_presentation_targets(p,targets,2,sizeof targets[0])==-410,"malformed-entire-group-rejected");
        for (int b=0;b<2;++b) for (int n=0;n<17;++n)
            authority_check(b2Distance(old_positions[b][n],b2Body_GetPosition(display_id(p->bodies[b],n)))==0 &&
                b2Distance(old_velocities[b][n],b2Body_GetLinearVelocity(display_id(p->bodies[b],n)))==0,"malformed-target-no-partial-mutation");
        targets[1].core_x=4;
        authority_check(dango_physics_world_apply_presentation_targets(p,targets,2,sizeof targets[0])==0,"valid-presentation-group");
        for (int b=0;b<2;++b) for (int n=0;n<17;++n)
        {
            b2Vec2 delta=b2Sub((b2Vec2){targets[b].core_x,targets[b].core_y},old_positions[b][16]);
            b2Vec2 velocity_delta=b2Sub((b2Vec2){targets[b].core_vx,targets[b].core_vy},old_velocities[b][16]);
            authority_check(b2Distance(b2Add(old_positions[b][n],delta),b2Body_GetPosition(display_id(p->bodies[b],n)))<=1e-6f &&
                b2Distance(b2Add(old_velocities[b][n],velocity_delta),b2Body_GetLinearVelocity(display_id(p->bodies[b],n)))<=1e-6f,"targets-preserve-relative-dynamics");
        }
        authority_check(dango_physics_world_enable_authority(p)==DANGO_AUTHORITY_MODE_CONFLICT &&
            dango_physics_world_get_authority_frame(p,&frame,sizeof frame)==DANGO_AUTHORITY_MODE_CONFLICT,"presentation-authority-role-conflict");
        /* The legacy export is also public and accepts the same world handle.
           Each action must fail before advancing any local presentation state. */
        for (int b=0;b<2;++b) for (int action=0;action<4;++action)
        {
            collision_review previous=*p;
            b2Vec2 positions[2][17],velocities[2][17];
            float cooldowns[2],legacy_output[126],expected_output[126];
            int legacy_contacts=0x1234;
            for (int body=0;body<2;++body)
            {
                cooldowns[body]=p->bodies[body]->seconds_since_jump;
                for (int n=0;n<17;++n)
                {
                    positions[body][n]=b2Body_GetPosition(display_id(p->bodies[body],n));
                    velocities[body][n]=b2Body_GetLinearVelocity(display_id(p->bodies[body],n));
                }
            }
            memset(legacy_output,0x5a,sizeof legacy_output);
            memcpy(expected_output,legacy_output,sizeof expected_output);
            memset(controls,0,sizeof controls); controls[b*6+action]=1;
            authority_check(dango_collision_step(p,authority_test_dt,controls,legacy_output,126,&legacy_contacts)==
                DANGO_AUTHORITY_MODE_CONFLICT,"legacy-presentation-actions-rejected");
            authority_check(memcmp(p,&previous,sizeof *p)==0 && legacy_contacts==0x1234 &&
                memcmp(legacy_output,expected_output,sizeof legacy_output)==0,"legacy-action-failure-no-world-or-output-mutation");
            for (int body=0;body<2;++body)
            {
                authority_check(cooldowns[body]==p->bodies[body]->seconds_since_jump,"legacy-action-failure-no-cooldown-mutation");
                for (int n=0;n<17;++n)
                    authority_check(b2Distance(positions[body][n],b2Body_GetPosition(display_id(p->bodies[body],n)))==0 &&
                        b2Distance(velocities[body][n],b2Body_GetLinearVelocity(display_id(p->bodies[body],n)))==0,
                        "legacy-action-failure-no-live-rig-mutation");
            }
        }
        memset(controls,0,sizeof controls);
        controls[0]=1;
        authority_check(authority_test_step(p,controls)==DANGO_AUTHORITY_MODE_CONFLICT,"presentation-actions-rejected");
        controls[0]=0;
        authority_check(authority_test_step(p,controls)==0,"presentation-neutral-step");
        dango_physics_world_destroy(p);
    }
    authority_test_publication_fault();
    authority_test_debug_role_guards();
    authority_check(b2GetByteCount()==initial_bytes,"native-allocation-returned");
    printf("{\"Suite\":\"authority-contracts\",\"Variant\":%d,\"Checks\":%d,\"Failures\":%d,\"SupportSamples\":%d,\"VelocitySamples\":%d}\n",
        DANGO_AUTHORITY_TEST_VARIANT,authority_test_checks,authority_test_failures,support_samples,velocity_samples);
    return authority_test_failures;
}

static void authority_cold_controls(int scenario,int tick,float* controls)
{
    memset(controls,0,18*sizeof(float));
    if (scenario==1 && tick>=60) { controls[0]=1; controls[6]=-1; }
    if (scenario==3 && tick==120) controls[2]=1;
}

int dango_authority_cold_restore_tests(void)
{
    int failures=0,divergent_scenarios=0;
    const char* names[5]={"floor","dynamic-collision","stack","airborne","removed-lower"};
    for (int scenario=0;scenario<5;++scenario)
    {
        int count=(scenario==0 || scenario==3) ? 1 : 2;
        collision_review* continuous=authority_test_create(count,scenario==2 || scenario==4 ? 1 : 0,true);
        collision_review* cold=authority_test_create(0,0,true);
        if (!continuous || !cold)
        {
            ++failures;
            if (continuous) dango_physics_world_destroy(continuous);
            if (cold) dango_physics_world_destroy(cold);
            continue;
        }
        int source_slots[3]={0},cold_slots[3]={0},body_count=0;
        float controls[18]={0};
        int warmup=scenario==3 ? 128 : scenario==1 ? 180 : 120;
        for (int tick=0;tick<warmup;++tick)
        {
            authority_cold_controls(scenario,tick,controls);
            if (authority_test_step(continuous,controls)!=0) ++failures;
        }
        if (scenario==4 && dango_physics_world_remove(continuous,continuous->entity_ids[0])!=0) ++failures;
        double initial_position_error=0,initial_velocity_error=0;
        /* Copy our own semantics and public poses/velocities, without hidden
           joint/contact impulses, sleep timers or graph state. */
        for (int source=0;source<continuous->capacity;++source) if (continuous->active[source])
        {
            int id=0;
            if (dango_physics_world_add(cold,continuous->anchors[2*source],continuous->anchors[2*source+1],&id)!=0) { ++failures; continue; }
            int target=body_count;
            source_slots[body_count]=source; cold_slots[body_count++]=target;
            cold->entity_ids[target]=continuous->entity_ids[source];
            cold->bodies[target]->seconds_since_jump=continuous->bodies[source]->seconds_since_jump;
            cold->bodies[target]->grounded=continuous->bodies[source]->grounded;
            for (int n=0;n<17;++n)
            {
                b2BodyId source_id=display_id(continuous->bodies[source],n),target_id=display_id(cold->bodies[target],n);
                b2Vec2 position=b2Body_GetPosition(source_id),velocity=b2Body_GetLinearVelocity(source_id);
                b2Body_SetTransform(target_id,position,b2Body_GetRotation(source_id));
                b2Body_SetLinearVelocity(target_id,velocity);
                b2Body_SetAngularVelocity(target_id,b2Body_GetAngularVelocity(source_id));
                b2Body_SetAwake(target_id,b2Body_IsAwake(source_id));
                initial_position_error=fmax(initial_position_error,b2Distance(position,b2Body_GetPosition(target_id)));
                initial_velocity_error=fmax(initial_velocity_error,b2Distance(velocity,b2Body_GetLinearVelocity(target_id)));
            }
        }
        cold->next_entity_id=continuous->next_entity_id;
        cold->physics_step=continuous->physics_step; cold->scene_revision=continuous->scene_revision;
        if (collision_read(cold,cold->snapshot)!=0) ++failures;
        authority_publish(cold,true);
        double maximum_position_error=0,maximum_velocity_error=0;
        int first_divergent_tick=-1,semantic_mismatches=0;
        for (int tick=0;tick<120;++tick)
        {
            authority_cold_controls(scenario,warmup+tick,controls);
            if (authority_test_step(continuous,controls)!=0 || authority_test_step(cold,controls)!=0) { ++failures; break; }
            double position_error=0,velocity_error=0;
            for (int b=0;b<body_count;++b) for (int n=0;n<17;++n)
            {
                b2BodyId source_id=display_id(continuous->bodies[source_slots[b]],n),target_id=display_id(cold->bodies[cold_slots[b]],n);
                b2Vec2 dp=b2Sub(b2Body_GetPosition(source_id),b2Body_GetPosition(target_id));
                b2Vec2 dv=b2Sub(b2Body_GetLinearVelocity(source_id),b2Body_GetLinearVelocity(target_id));
                position_error=fmax(position_error,fmax(fabs(dp.x),fabs(dp.y)));
                velocity_error=fmax(velocity_error,fmax(fabs(dv.x),fabs(dv.y)));
            }
            dango_authority_frame source_frame={0},cold_frame={0};
            dango_physics_world_get_authority_frame(continuous,&source_frame,sizeof source_frame);
            dango_physics_world_get_authority_frame(cold,&cold_frame,sizeof cold_frame);
            bool semantics_match=source_frame.error_code==cold_frame.error_code;
            for (int b=0;b<body_count;++b)
                semantics_match=semantics_match && source_frame.bodies[b].support_entity_id==cold_frame.bodies[b].support_entity_id &&
                    source_frame.bodies[b].jump_result==cold_frame.bodies[b].jump_result;
            if (!semantics_match) ++semantic_mismatches;
            if (first_divergent_tick<0 && (position_error>1e-5 || velocity_error>1e-4 || !semantics_match)) first_divergent_tick=tick+1;
            maximum_position_error=fmax(maximum_position_error,position_error); maximum_velocity_error=fmax(maximum_velocity_error,velocity_error);
        }
        if (initial_position_error>1e-6 || initial_velocity_error>1e-6) ++failures;
        if (first_divergent_tick>=0) ++divergent_scenarios;
        printf("{\"Suite\":\"public-pose-cold-restore\",\"Scenario\":\"%s\",\"InitialPositionError\":%.9g,\"InitialVelocityError\":%.9g,\"FutureMaximumPositionError\":%.9g,\"FutureMaximumVelocityError\":%.9g,\"FirstDivergentTick\":%d,\"SemanticMismatches\":%d,\"CompleteRecoveryGatePassed\":false}\n",
            names[scenario],initial_position_error,initial_velocity_error,maximum_position_error,maximum_velocity_error,first_divergent_tick,semantic_mismatches);
        dango_physics_world_destroy(continuous); dango_physics_world_destroy(cold);
    }
    printf("{\"Suite\":\"cold-restore-summary\",\"Scenarios\":5,\"DivergentScenarios\":%d,\"HarnessFailures\":%d}\n",divergent_scenarios,failures);
    return failures;
}
#endif

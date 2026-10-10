#ifndef DANGO_AUTHORITY_INTERNAL_H
#define DANGO_AUTHORITY_INTERNAL_H

/* Included after the existing read-only support oracle. Only the public Box2D
   API is used; no solver-private structures or recovery shortcuts are used. */
static bool authority_role_supported(const collision_review* p)
{
    return p->capacity<=2 && !p->platform_enabled && !p->ball_scene &&
        !p->collection_mode && !p->collection_count && p->preview_jump_speed_scale==1.0f &&
        p->legacy_interaction_flags==0 && !p->disable_contact_for_test && !p->disable_damping_for_test;
}

static bool authority_refresh(collision_review* p)
{
    if (!p->authority_enabled || p->error || p->authority_error) return false;
    if (!authority_role_supported(p) || p->presentation_mode) return false;
#ifdef DANGO_AUTHORITY_CONTRACT_TESTS
    if (p->test_authority_publication_fault) return false;
#endif
    dango_authority_frame frame={.abi_version=DANGO_AUTHORITY_ABI_VERSION,
        .frame_size=sizeof(dango_authority_frame),.tick=p->physics_step,.scene_revision=p->scene_revision};
    int output_slots[3]={0};
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot])
    {
        dango_authority_body* state=&frame.bodies[frame.body_count];
        output_slots[frame.body_count++]=slot;
        b2Vec2 velocity=b2Body_GetLinearVelocity(p->bodies[slot]->core_body_id);
        state->entity_id=p->entity_ids[slot];
        state->core_x=p->snapshot[slot*42+32]; state->core_y=p->snapshot[slot*42+33];
        state->core_vx=velocity.x; state->core_vy=velocity.y;
        state->jump_result=p->authority_jump_results[slot];
        state->cooldown_remaining=fmaxf(0,p->config.jump_cooldown_seconds-p->bodies[slot]->seconds_since_jump);
        if (!isfinite(state->core_x) || !isfinite(state->core_y) || !b2IsValidVec2(velocity) ||
            !isfinite(state->cooldown_remaining)) return false;
        if (p->snapshot[slot*42+39]!=0) state->flags|=DANGO_AUTHORITY_FLOOR_GROUNDED;
        int support=collision_support_slot(p,slot);
        if (support>=0)
        {
            state->support_entity_id=p->entity_ids[support];
            state->flags|=DANGO_AUTHORITY_DANGO_SUPPORTED;
        }
        if ((state->flags&(DANGO_AUTHORITY_FLOOR_GROUNDED|DANGO_AUTHORITY_DANGO_SUPPORTED)) &&
            state->cooldown_remaining==0) state->flags|=DANGO_AUTHORITY_CAN_JUMP;
    }
    for (int a=0;a<frame.body_count;++a) for (int b=a+1;b<frame.body_count;++b)
    {
        display_mesh mesh[2]; b2Vec2 normal; int ia,ib;
        int slot_a=output_slots[a],slot_b=output_slots[b];
        if (!display_build(p->bodies[slot_a],p->config.node_radius,&mesh[0]) ||
            !display_build(p->bodies[slot_b],p->config.node_radius,&mesh[1])) return false;
        float gap=display_separation(&mesh[0],&mesh[1],&normal,&ia,&ib,.008f);
        if (!isfinite(gap)) return false;
        if (gap>.008f) continue;
        dango_authority_pair* pair=&frame.pairs[frame.pair_count++];
        pair->entity_a=frame.bodies[a].entity_id; pair->entity_b=frame.bodies[b].entity_id;
        pair->flags=DANGO_AUTHORITY_PAIR_TOUCHING;
        pair->nx=normal.x; pair->ny=normal.y; pair->gap=gap;
        pair->minimum_projected_width=fminf(display_projected_width(&mesh[0],normal),display_projected_width(&mesh[1],normal));
        if (!b2IsValidVec2(normal) || fabsf(b2Length(normal)-1)>1e-4f ||
            !isfinite(pair->minimum_projected_width) || pair->minimum_projected_width<=0) return false;
        if (frame.bodies[a].support_entity_id==pair->entity_b)
        {
            pair->flags|=DANGO_AUTHORITY_PAIR_SUPPORTS_A;
            frame.bodies[a].support_nx=-normal.x; frame.bodies[a].support_ny=-normal.y;
            frame.bodies[a].support_gap=gap;
        }
        if (frame.bodies[b].support_entity_id==pair->entity_a)
        {
            pair->flags|=DANGO_AUTHORITY_PAIR_SUPPORTS_B;
            frame.bodies[b].support_nx=normal.x; frame.bodies[b].support_ny=normal.y;
            frame.bodies[b].support_gap=gap;
        }
    }
#if defined(DANGO_AUTHORITY_CONTRACT_TESTS) && DANGO_AUTHORITY_TEST_VARIANT == 1
    for (int i=0;i<frame.body_count;++i)
    {
        frame.bodies[i].support_entity_id=0;
        frame.bodies[i].flags&=~(uint32_t)DANGO_AUTHORITY_DANGO_SUPPORTED;
    }
#endif
#if defined(DANGO_AUTHORITY_CONTRACT_TESTS) && DANGO_AUTHORITY_TEST_VARIANT == 2
    for (int i=0;i<frame.body_count;++i) frame.bodies[i].core_vx=frame.bodies[i].core_vy=0;
#endif
    p->authority_snapshot=frame;
    return true;
}

static void authority_publish(collision_review* p,bool clear_jump)
{
    if (!p->authority_enabled) return;
    if (clear_jump) memset(p->authority_jump_results,0,sizeof p->authority_jump_results);
    if (!p->error && !authority_refresh(p)) p->authority_error=DANGO_AUTHORITY_INVALID_OUTPUT;
}

DANGO_API int dango_physics_get_authority_abi_version(void) { return DANGO_AUTHORITY_ABI_VERSION; }
DANGO_API int dango_physics_get_authority_frame_size(void) { return (int)sizeof(dango_authority_frame); }
DANGO_API int dango_physics_get_authority_capabilities(void) { return DANGO_AUTHORITY_SEMANTIC_FRAMES; }
DANGO_API int dango_physics_get_presentation_target_size(void) { return (int)sizeof(dango_presentation_target); }

DANGO_API int dango_physics_world_enable_authority(void* handle)
{
    collision_review* p=handle;
    if (!p) return -410;
    if (p->presentation_mode) return DANGO_AUTHORITY_MODE_CONFLICT;
    if (!authority_role_supported(p)) return DANGO_AUTHORITY_UNSUPPORTED;
    if (p->error) return p->error;
    if (p->authority_enabled) return p->authority_error;
    if (p->physics_step!=0) return -410;
    p->authority_enabled=true;
    if (!authority_refresh(p))
    {
        p->authority_enabled=false;
        return DANGO_AUTHORITY_INVALID_OUTPUT;
    }
    return 0;
}

DANGO_API int dango_physics_world_get_authority_frame(void* handle,dango_authority_frame* frame,int frame_size)
{
    collision_review* p=handle;
    if (!p || !frame || frame_size!=(int)sizeof *frame) return -410;
    if (p->presentation_mode) return DANGO_AUTHORITY_MODE_CONFLICT;
    if (!p->authority_enabled || !authority_role_supported(p)) return DANGO_AUTHORITY_UNSUPPORTED;
    *frame=p->authority_snapshot;
    frame->error_code=p->error ? p->error : p->authority_error;
    return 0;
}

DANGO_API int dango_physics_world_export_checkpoint(void* handle,void* buffer,int capacity,int* written)
{
    if (!handle || !written || capacity<0 || (capacity>0 && !buffer)) return -410;
    *written=0;
    return DANGO_AUTHORITY_UNSUPPORTED;
}

DANGO_API int dango_physics_world_import_checkpoint(void* handle,const void* buffer,int size)
{
    if (!handle || size<0 || (size>0 && !buffer)) return -410;
    return DANGO_AUTHORITY_UNSUPPORTED;
}

DANGO_API int dango_physics_world_apply_presentation_targets(void* handle,
    const dango_presentation_target* targets,int count,int target_size)
{
    collision_review* p=handle;
    if (!p || !targets || target_size!=(int)sizeof *targets || count!=p->count || count<1 || count>2) return -410;
    if (p->authority_enabled) return DANGO_AUTHORITY_MODE_CONFLICT;
    if (!authority_role_supported(p)) return DANGO_AUTHORITY_UNSUPPORTED;
    if (p->error) return p->error;
    int slots[3]={0},seen[3]={0};
    b2Vec2 deltas[3],velocity_deltas[3];
    for (int i=0;i<count;++i)
    {
        const dango_presentation_target* target=&targets[i];
        b2Vec2 position={target->core_x,target->core_y},velocity={target->core_vx,target->core_vy};
        if (!b2IsValidVec2(position) || !b2IsValidVec2(velocity) || fabsf(position.x)>1024 || fabsf(position.y)>1024 ||
            b2Length(velocity)>p->config.maximum_speed) return -410;
        int slot=-1;
        for (int s=0;s<p->capacity;++s) if (p->active[s] && p->entity_ids[s]==target->entity_id) { slot=s; break; }
        if (slot<0 || seen[slot]) return -412;
        seen[slot]=1; slots[i]=slot;
        deltas[i]=b2Sub(position,b2Body_GetPosition(p->bodies[slot]->core_body_id));
        velocity_deltas[i]=b2Sub(velocity,b2Body_GetLinearVelocity(p->bodies[slot]->core_body_id));
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[slot],n);
            if (!b2IsValidVec2(b2Add(b2Body_GetPosition(id),deltas[i])) ||
                !b2IsValidVec2(b2Add(b2Body_GetLinearVelocity(id),velocity_deltas[i]))) return -410;
        }
    }
    /* No failure-capable operation follows the complete validation pass. */
    for (int i=0;i<count;++i) for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[slots[i]],n);
        b2Body_SetTransform(id,b2Add(b2Body_GetPosition(id),deltas[i]),b2Rot_identity);
        b2Body_SetLinearVelocity(id,b2Add(b2Body_GetLinearVelocity(id),velocity_deltas[i]));
    }
    p->presentation_mode=true;
    return 0;
}

#endif

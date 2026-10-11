/* Isolated executable: includes the unchanged production source to inspect
   Box2D public node getters and executable-only publication failure injection.
   This does not generate a replacement DLL or introduce exports. */
#include "../../../native/box2d/dango_box2d_bridge.c"
#include "../../../native/box2d/dango_collision_review.c"

int main(int argc,char** argv)
{
    bool wrong=argc==2 && strcmp(argv[1],"faultAutoRebirth")==0;
    bool producer=argc>=2 && strcmp(argv[1],"faultProducer")==0;
    int prefix=producer && argc==3?atoi(argv[2]):1;
    if(prefix<1 || prefix>10000) return 2;
    int bytes=b2GetByteCount(),checks=0,failures=0,created=0,disposed=0;
#define CHECK(value,name) do { ++checks; if(!(value)) { ++failures; fprintf(stderr,"FAIL %s\n",name); } } while(0)
    collision_review* p=authority_test_create(0,0,true);
    CHECK(p!=NULL,"create"); if(!p) return 1; ++created;
    CHECK(dango_physics_world_set_horizontal_bounds(p,-13.2f,13.2f)==0,"bounds");
    int ids[2]={0};
    for(int i=0;i<2;++i) CHECK(dango_physics_world_add(p,i==0?-1.4f:1.4f,0,&ids[i])==0,"whole-group-add");
    dango_authority_frame tick0={0};
    CHECK(dango_physics_world_get_authority_frame(p,&tick0,sizeof tick0)==0 && tick0.tick==0 && tick0.body_count==2,"tick-zero-complete");
    for(int body=0;body<2;++body)
    {
        CHECK(tick0.bodies[body].core_vx==0 && tick0.bodies[body].core_vy==0 &&
            tick0.bodies[body].support_entity_id==0 && tick0.bodies[body].cooldown_remaining==0,"tick-zero-semantics");
        for(int n=0;n<17;++n)
        {
            b2Vec2 v=b2Body_GetLinearVelocity(display_id(p->bodies[body],n));
            CHECK(v.x==0 && v.y==0 && b2Body_GetAngularVelocity(display_id(p->bodies[body],n))==0,"tick-zero-all-node-velocity");
        }
    }
    float controls[18]={0};
    CHECK(authority_test_step(p,controls)==0,"first-neutral-step");
    dango_authority_frame before={0},after={0};
    CHECK(dango_physics_world_get_authority_frame(p,&before,sizeof before)==0 && before.tick==1,"first-healthy-tick");
    CHECK(before.bodies[0].core_vy<0 && before.bodies[1].core_vy<0,"original-gravity-not-cleared");
    for(int step=1;step<prefix;++step) CHECK(authority_test_step(p,controls)==0,"source-prefix-health");
    CHECK(dango_physics_world_get_authority_frame(p,&before,sizeof before)==0 && before.tick==(uint64_t)prefix,"source-last-safe-tick");
    b2Vec2 positions[2][17],velocities[2][17];
    for(int b=0;b<2;++b) for(int n=0;n<17;++n)
    { positions[b][n]=b2Body_GetPosition(display_id(p->bodies[b],n)); velocities[b][n]=b2Body_GetLinearVelocity(display_id(p->bodies[b],n)); }
    p->test_authority_publication_fault=true;
    authority_publish(p,false);
    p->test_authority_publication_fault=false;
    if(wrong) { CHECK(dango_physics_world_reset(p)==0,"wrong-reset-executed"); }
    CHECK(dango_physics_world_get_authority_frame(p,&after,sizeof after)==0 &&
        after.error_code==DANGO_AUTHORITY_INVALID_OUTPUT,"fault-visible-not-reborn");
    int observed_error=after.error_code;
    after.error_code=before.error_code;
    CHECK(memcmp(&before,&after,sizeof before)==0,"last-safe-entire-frame-preserved");
    CHECK(authority_test_step(p,controls)==DANGO_AUTHORITY_INVALID_OUTPUT,"fault-next-step-rejected");
    for(int b=0;b<2;++b) for(int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[b],n);
        b2Vec2 pos=b2Body_GetPosition(id),vel=b2Body_GetLinearVelocity(id);
        CHECK(memcmp(&positions[b][n],&pos,sizeof pos)==0 && memcmp(&velocities[b][n],&vel,sizeof vel)==0,"fault-not-reset-or-stepped");
    }
    CHECK(dango_physics_world_destroy(p)==0,"dispose"); ++disposed;
    if(!producer && !wrong)
    {
        /* Real Add can mutate membership before semantic publication fails.
           Keep the historical last-safe group, stop; do not claim rollback. */
        p=authority_test_create(0,0,true);CHECK(p!=NULL,"add-fault-create");
        if(p)
        {
            ++created;int first=0,second=0;
            CHECK(dango_physics_world_set_horizontal_bounds(p,-13.2f,13.2f)==0,"add-fault-bounds");
            CHECK(dango_physics_world_add(p,-1.4f,0,&first)==0 && authority_test_step(p,controls)==0,"add-fault-existing-healthy");
            dango_authority_frame safe={0},fault={0};
            CHECK(dango_physics_world_get_authority_frame(p,&safe,sizeof safe)==0,"add-fault-last-safe-read");
            p->test_authority_publication_fault=true;
            int code=dango_physics_world_add(p,1.4f,0,&second);
            p->test_authority_publication_fault=false;
            CHECK(code==DANGO_AUTHORITY_INVALID_OUTPUT && p->count==2 && second>0,"real-add-failure-is-not-rollback");
            CHECK(dango_physics_world_get_authority_frame(p,&fault,sizeof fault)==0 && fault.error_code==DANGO_AUTHORITY_INVALID_OUTPUT,"add-fault-latched");
            fault.error_code=safe.error_code;
            CHECK(memcmp(&safe,&fault,sizeof safe)==0 && fault.body_count==1,"add-fault-retains-entire-prior-safe-group");
            CHECK(authority_test_step(p,controls)==DANGO_AUTHORITY_INVALID_OUTPUT,"add-fault-no-further-step");
            CHECK(dango_physics_world_destroy(p)==0,"add-fault-owner-dispose");++disposed;
        }
    }
    CHECK(b2GetByteCount()==bytes,"allocation-returned");
    printf("{\"Version\":1,\"Checks\":%d,\"Failures\":%d,\"Created\":%d,\"Disposed\":%d,\"Before\":%d,\"After\":%d,\"LastSafeTick\":%llu,\"NativeErrorCode\":%d,\"Steps\":%d,\"Pass\":%s}\n",checks,failures,created,disposed,bytes,b2GetByteCount(),(unsigned long long)before.tick,observed_error,prefix,failures?"false":"true");
    return failures?1:0;
}



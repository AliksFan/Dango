// Private, bounded stdin/stdout protocol for the isolated single-rig review.
#include <string.h>
static void review_frame(dango_self_soft_body* body,sleep_probe* probe,int frame)
{
    b2Vec2 core=b2Body_GetPosition(body->core_body_id);
    float speed=0;
    for (int i=0;i<17;++i) speed=fmaxf(speed,b2Length(b2Body_GetLinearVelocity(rig_id(body,i))));
    printf("frame %d %d %d %d %d %.9g %.9g %.9g %d",frame,probe->failed,probe->sleeping,
        awake_count(body),probe->stable_frames,(double)speed,(double)core.x,(double)core.y,body->selected_drag_node);
    for (int i=0;i<16;++i)
    {
        b2Vec2 local=b2Sub(b2Body_GetPosition(body->node_body_ids[i]),core);
        printf(" %.9g %.9g",(double)local.x,(double)local.y);
    }
    puts(""); fflush(stdout);
}

static int dynamic_review(bool test_fault)
{
    ground_candidate=true;ground_variant=4;preserve_joint_impulses=true;interactive_targets=true;
    diagnostic_cache_reset=0;diagnostic_dt=0;
    void* handle=NULL;
    dango_self_physics_config config=configuration();
    sleep_probe probe={0};
    int frame=0;
    char line[256];
    for (;;)
    {
        if (!handle)
        {
            CHECK(dango_box2d_self_soft_body_create(&config,&handle)==0);
            dango_self_soft_body* created=handle;
            for (int i=0;i<17;++i) trace_ids[i]=rig_id(created,i);
            ground_candidate_shape=created->ground_shape_id;b2Shape_SetFriction(ground_candidate_shape,0);
            memset(&probe,0,sizeof(probe));probe.speed_threshold=.02f;frame=0;
            puts("ready 1");fflush(stdout);
        }
        dango_self_soft_body* body=handle;
        if (!fgets(line,sizeof(line),stdin)) break;
        if (!strchr(line,'\n') && !feof(stdin))
        {
            int ch;while ((ch=getchar())!='\n' && ch!=EOF) { }
            puts("error invalid");fflush(stdout);continue;
        }
        line[strcspn(line,"\r\n")]=0;
        if (strcmp(line,"quit")==0) break;
        if (strcmp(line,"reset")==0)
        {
            int bytes;CHECK(dango_box2d_self_soft_body_destroy(handle,&bytes)==0 && bytes==0);
            handle=NULL;extra_downward=0;extra_jump=0;continue;
        }
        if (test_fault && strcmp(line,"fault")==0)
        {
            b2Body_SetTransform(body->node_body_ids[0],b2Body_GetPosition(body->core_body_id),b2Rot_identity);
            probe.failed=true;review_frame(body,&probe,frame);continue;
        }
        float horizontal,down,x,y,jump,drag;char trailing;
        if (strncmp(line,"step ",5)!=0 || sscanf(line,"step %f %f %f %f %f %f %c",&horizontal,&down,&jump,&drag,&x,&y,&trailing)!=6 ||
            !isfinite(horizontal) || !isfinite(down) || !isfinite(x) || !isfinite(y) ||
            fabsf(horizontal)>1 || down<0 || down>1 || (jump!=0 && jump!=1) || (drag!=0 && drag!=1) || fabsf(x)>100 || fabsf(y)>100)
        { puts("error invalid");fflush(stdout);continue; }
        if (!probe.failed)
        {
            extra_downward=down;extra_jump=(int)jump;interactive_target_x=x;interactive_target_y=y;
            if (!tick(body,&config,&probe,0,0,horizontal,(int)drag,1.0f/60.0f)) probe.failed=true;
            ++frame;
        }
        review_frame(body,&probe,frame);
    }
    int bytes;CHECK(dango_box2d_self_soft_body_destroy(handle,&bytes)==0 && bytes==0);
    puts("bye 0");fflush(stdout);return 0;
}

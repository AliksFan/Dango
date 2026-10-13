/* Included after the legacy solvers. Collection mode borrows their rig,
   display geometry and ball impulse equations in the same native world. */
#ifndef DANGO_FURNITURE_COLLECTION_H
#define DANGO_FURNITURE_COLLECTION_H

#ifndef DANGO_FURNITURE_COLLECTION_TEST_VARIANT
#define DANGO_FURNITURE_COLLECTION_TEST_VARIANT 0
#endif

_Static_assert(sizeof(dango_furniture_definition)==96,"Collection definition ABI");
_Static_assert(sizeof(dango_furniture_collection_item)==400,"Collection item ABI");
_Static_assert(sizeof(dango_furniture_collection_frame)==13944,"Collection frame ABI");
_Static_assert(sizeof(dango_furniture_collection_probe)==40,"Collection probe ABI");

static bool collection_definition_valid(const dango_furniture_definition* d)
{
    if (!d || d->reserved || d->padding) return false;
    if (d->kind==1) return platform_valid(&d->platform);
    if (d->kind!=2) return false;
    const dango_ball_definition* b=&d->ball;
    return isfinite(b->x) && isfinite(b->y) && fabs(b->x)<=1024 && fabs(b->y)<=1024 &&
        isfinite(b->radius) && b->radius>=.05 && b->radius<=4 &&
        isfinite(b->mass) && b->mass>=.001 && b->mass<=10 &&
        isfinite(b->floor_friction) && b->floor_friction>=0 && b->floor_friction<=1.5 &&
        isfinite(b->dango_friction) && b->dango_friction>=0 && b->dango_friction<=1.5 &&
        isfinite(b->floor_restitution) && b->floor_restitution>=0 && b->floor_restitution<=1 &&
        isfinite(b->dango_restitution) && b->dango_restitution>=0 && b->dango_restitution<=1;
}

static double collection_circle_rectangle(double x,double y,double radius,
    const dango_static_platform* p,double* nx,double* ny)
{
    double left=(4.0/3)*(p->x-p->width*.5),right=(4.0/3)*(p->x+p->width*.5);
    double bottom=p->y-p->height*.5,top=p->y+p->height*.5;
    double dx=x-fmax(left,fmin(right,x)),dy=y-fmax(bottom,fmin(top,y)),length=hypot(dx,dy);
    if (length>1e-12) { *nx=dx/length; *ny=dy/length; return length-radius; }
    double nearest=x-left; *nx=-1; *ny=0;
    if (right-x<nearest) { nearest=right-x; *nx=1; *ny=0; }
    if (y-bottom<nearest) { nearest=y-bottom; *nx=0; *ny=-1; }
    if (top-y<nearest) { nearest=top-y; *nx=0; *ny=1; }
    return -nearest-radius;
}

static double collection_definition_gap(const dango_furniture_definition* a,
    const dango_furniture_definition* b)
{
    if (a->kind==1 && b->kind==1)
        return fmax(fabs(a->platform.x-b->platform.x)-(a->platform.width+b->platform.width)*.5,
            fabs(a->platform.y-b->platform.y)-(a->platform.height+b->platform.height)*.5);
    if (a->kind==2 && b->kind==2)
        return hypot(a->ball.x-b->ball.x,a->ball.y-b->ball.y)-a->ball.radius-b->ball.radius;
    const dango_furniture_definition* ball=a->kind==2 ? a : b;
    const dango_furniture_definition* platform=a->kind==1 ? a : b;
    double nx,ny;
    return collection_circle_rectangle(ball->ball.x,ball->ball.y,ball->ball.radius,&platform->platform,&nx,&ny);
}

static bool collection_refresh(collision_review* p)
{
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        bool grounded=collision_platform_floor(p,p->bodies[r]);
        p->collection_support_friction[r]=-1;
        display_mesh mesh; if (!display_build(p->bodies[r],p->body_config[r].node_radius,&mesh)) return false;
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==1)
        {
            dango_static_platform* platform=&p->collection[i].definition.platform;
            dango_platform_contact contact=platform_separation(&mesh,platform);
            if (!isfinite(contact.gap)) return false;
            b2Vec2 core=b2Body_GetPosition(p->bodies[r]->core_body_id),velocity=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id);
            bool support=platform_support_eligible(&contact,core.y,velocity.y,platform);
            grounded=grounded || support;
            if (support) p->collection_support_friction[r]=fmaxf(p->collection_support_friction[r],platform->friction);
        }
        p->bodies[r]->grounded=grounded;
    }
    return true;
}

static void collection_publish(collision_review* p)
{
    dango_furniture_collection_frame* f=&p->collection_snapshot;
    memset(f,0,sizeof *f); collision_furniture_read_core(p,&f->bodies);
    f->bodies.platform_instance=0; f->bodies.platform_enabled=0;
    f->bodies.platform=(dango_static_platform){0};
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance)
    {
        dango_collection_item* item=&p->collection[i];
        dango_furniture_collection_item* output=&f->items[f->item_count++];
        output->instance=item->instance; output->definition=item->definition;
        if (item->definition.kind==2)
        {
            item->ball.state.scene_revision=p->scene_revision; item->ball.state.step=p->physics_step;
            item->dango_contact.scene_revision=item->surface_contact.scene_revision=p->scene_revision;
            item->dango_contact.step=item->surface_contact.step=p->physics_step;
            output->ball=item->ball.state; output->dango_contact=item->dango_contact; output->surface_contact=item->surface_contact;
        }
    }
}

static void collection_begin(collision_review* p)
{
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==2)
    {
        p->collection[i].dango_contact=(dango_ball_contact){0};
        p->collection[i].surface_contact=(dango_ball_contact){.object_id=-1};
    }
}

static double collection_minimum_gap(collision_review* p)
{
    double minimum=1024;
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance)
    {
        dango_collection_item* a=&p->collection[i];
        if (a->definition.kind==1)
        {
            for (int r=0;r<p->capacity;++r) if (p->active[r])
            {
                display_mesh mesh; if (!display_build(p->bodies[r],p->body_config[r].node_radius,&mesh)) return -INFINITY;
                minimum=fmin(minimum,platform_separation(&mesh,&a->definition.platform).gap);
            }
            continue;
        }
        double x=a->ball.state.x,y=a->ball.state.y,radius=a->ball.definition.radius,left,right;
        collision_ball_floor_range(p,&left,&right);
        minimum=fmin(minimum,fmin(y-radius-p->config.ground_height,fmin(x-radius-left,right-x-radius)));
        for (int r=0;r<p->capacity;++r) if (p->active[r])
        {
            b2Vec2 core=b2Body_GetPosition(p->bodies[r]->core_body_id);
            double distance=hypot(x-(4.0/3)*core.x,y-core.y);
            if (distance>radius+1.7) { minimum=fmin(minimum,distance-radius-1.7); continue; }
            dango_ball_geometry geometry; dango_ball_geometry_result contact;
            collision_ball_geometry(p,r,x,y,radius,&geometry);
            if (!dango_ball_geometry_contact(&geometry,&contact)) return -INFINITY;
            minimum=fmin(minimum,contact.gap);
        }
        for (int j=0;j<DANGO_FURNITURE_COLLECTION_MAX;++j) if (i!=j && p->collection[j].instance)
        {
            dango_collection_item* b=&p->collection[j];
            double gap,nx,ny;
            if (b->definition.kind==1) gap=collection_circle_rectangle(x,y,radius,&b->definition.platform,&nx,&ny);
            else gap=hypot(x-b->ball.state.x,y-b->ball.state.y)-radius-b->ball.definition.radius;
            minimum=fmin(minimum,gap);
        }
    }
    return minimum;
}

static bool collection_ball_pair(collision_review* p,int i,int j,int* touched_a,int* touched_b)
{
#if DANGO_FURNITURE_COLLECTION_TEST_VARIANT == 1
    (void)p; (void)i; (void)j; (void)touched_a; (void)touched_b; return true;
#else
    dango_collection_item* a=&p->collection[i],*b=&p->collection[j];
    dango_ball_state* sa=&a->ball.state,*sb=&b->ball.state;
    double dx=sb->x-sa->x,dy=sb->y-sa->y,length=hypot(dx,dy);
    double ra=a->ball.definition.radius,rb=b->ball.definition.radius,gap=length-ra-rb;
    bool* was=&p->collection_pair_touch[i][j];
    if (gap>.001) { *was=false; return true; }
    double nx=length>1e-12 ? dx/length : 1,ny=length>1e-12 ? dy/length : 0,tx=-ny,ty=nx;
    double ma=a->ball.definition.mass,mb=b->ball.definition.mass,ia=.5*ma*ra*ra,ib=.5*mb*rb*rb;
    double vn=(sb->vx-sa->vx)*nx+(sb->vy-sa->vy)*ny;
    double vt=(sb->vx-sa->vx)*tx+(sb->vy-sa->vy)*ty-ra*sa->omega-rb*sb->omega;
    dango_ball_impulse impulse;
    double restitution=fmin(a->ball.definition.dango_restitution,b->ball.definition.dango_restitution);
    double friction=sqrt(a->ball.definition.dango_friction*b->ball.definition.dango_friction);
    if (!dango_ball_contact_impulse(1/ma+1/mb,0,1/ma+1/mb+ra*ra/ia+rb*rb/ib,
        vn,vt,dango_ball_restitution_target(-vn,restitution,!*was),friction,&impulse)) return false;
    *was=true;
    if (gap<-.0001 || impulse.normal>1e-8 || fabs(impulse.tangent)>1e-8)
    { sa->sleeping=sb->sleeping=0; a->ball.sleep_seconds=b->ball.sleep_seconds=0; *touched_a=*touched_b=1; }
    double ix=nx*impulse.normal+tx*impulse.tangent,iy=ny*impulse.normal+ty*impulse.tangent;
    sa->vx-=ix/ma; sa->vy-=iy/ma; sb->vx+=ix/mb; sb->vy+=iy/mb;
    sa->omega-=ra*impulse.tangent/ia; sb->omega-=rb*impulse.tangent/ib;
    double correction=fmin(.01,fmax(0,.0001-gap))/(1/ma+1/mb);
    sa->x-=nx*correction/ma; sa->y-=ny*correction/ma;
    sb->x+=nx*correction/mb; sb->y+=ny*correction/mb;
    return true;
#endif
}

static void collection_ball_settle(collision_review* p,dango_collection_item* item,double dt,int touched,
    double normal_start,double tangent_start)
{
    dango_ball_state* s=&item->ball.state;
    /* The accepted ball's floor rolling resistance and native sleep thresholds. */
    if (!s->sleeping && item->ball.floor_touch)
    {
        double radius=item->ball.definition.radius,mass=item->ball.definition.mass,inertia=.5*mass*radius*radius;
        double limit=.02*mass*DANGO_ROUND_BALL_GRAVITY_SCALE*p->config.gravity*radius*dt/(inertia+mass*radius*radius);
        if (item->ball.definition.floor_friction>0 && fabs(s->vx+radius*s->omega)<.002)
        {
            double available=fmax(0,item->ball.definition.floor_friction*(item->surface_contact.normal_impulse-normal_start)-
                fabs(item->surface_contact.tangent_impulse-tangent_start));
            limit=fmin(limit,available/(mass*radius));
            double delta=fmax(-limit,fmin(limit,s->omega));
            s->omega-=delta; s->vx+=radius*delta;
            item->surface_contact.tangent_impulse-=mass*radius*delta;
        }
        double gap=s->y-radius-p->config.ground_height;
        if (hypot(s->vx,s->vy)<=.02 && fabs(s->omega)<=.05 && fabs(gap)<=.001 && !touched)
        {
            item->ball.sleep_seconds+=dt;
            if (item->ball.sleep_seconds>=.5) { s->sleeping=1; s->vx=s->vy=s->omega=0; }
        }
        else item->ball.sleep_seconds=0;
    }
}

static bool collection_substep(collision_review* p,double dt,int* touched)
{
    dango_native_ball saved=p->ball;
    dango_ball_contact saved_dango=p->ball_dango_contact,saved_floor=p->ball_floor_contact;
    double normal_start[DANGO_FURNITURE_COLLECTION_MAX]={0},tangent_start[DANGO_FURNITURE_COLLECTION_MAX]={0};
    int ball_touched[DANGO_FURNITURE_COLLECTION_MAX]={0};
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==2)
    {
        dango_ball_state* s=&p->collection[i].ball.state;
        normal_start[i]=p->collection[i].surface_contact.normal_impulse;
        tangent_start[i]=p->collection[i].surface_contact.tangent_impulse;
        if (!s->sleeping && dt>0)
        { s->vy-=DANGO_ROUND_BALL_GRAVITY_SCALE*p->config.gravity*dt; s->x+=s->vx*dt; s->y+=s->vy*dt;
          s->angle+=s->omega*dt; ++s->integrations; }
    }
    bool valid=false;
    for (int scan=0;scan<96;++scan)
    {
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==1)
            for (int r=0;r<p->capacity;++r) if (p->active[r] && !COLLISION_RESTING(p,r))
                if (!platform_contact_solve(p->bodies[r],&p->body_config[r],&p->collection[i].definition.platform,touched)) goto finish;
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==2)
        {
            dango_collection_item* item=&p->collection[i];
            p->ball=item->ball; p->ball_dango_contact=item->dango_contact; p->ball_floor_contact=item->surface_contact;
            if (!collision_ball_valid(p) || !collision_ball_boundaries(p)) goto finish;
            for (int r=0;r<p->capacity;++r) if (p->active[r] && !COLLISION_RESTING(p,r))
            {
                b2Vec2 core=b2Body_GetPosition(p->bodies[r]->core_body_id);
                double distance=hypot(p->ball.state.x-(4.0/3)*core.x,p->ball.state.y-core.y);
                if (distance>p->ball.definition.radius+1.7)
                { p->ball.dango_touch=false; p->ball_dango_contact.gap=distance-p->ball.definition.radius-1.7; continue; }
                if (!collision_ball_dango(p,r,&ball_touched[i])) goto finish;
            }
            for (int j=0;j<DANGO_FURNITURE_COLLECTION_MAX;++j) if (p->collection[j].instance && p->collection[j].definition.kind==1)
            {
                double nx,ny,gap=collection_circle_rectangle(p->ball.state.x,p->ball.state.y,p->ball.definition.radius,
                    &p->collection[j].definition.platform,&nx,&ny);
                if (gap<=.001)
                {
#if DANGO_FURNITURE_COLLECTION_TEST_VARIANT != 2
                    dango_ball_contact contact={.object_id=(int)p->collection[j].instance};
                    if (!collision_ball_boundary(p,nx,ny,gap,&item->platform_touch[j],&contact)) goto finish;
                    if (gap<0 || contact.normal_impulse>0) ball_touched[i]=1;
#endif
                }
                else item->platform_touch[j]=false;
            }
            item->ball=p->ball; item->dango_contact=p->ball_dango_contact; item->surface_contact=p->ball_floor_contact;
        }
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==2)
            for (int j=i+1;j<DANGO_FURNITURE_COLLECTION_MAX;++j) if (p->collection[j].instance && p->collection[j].definition.kind==2)
                if (!collection_ball_pair(p,i,j,&ball_touched[i],&ball_touched[j])) goto finish;
        double gap=collection_minimum_gap(p);
        if (gap>=-1e-5) { valid=true; break; }
        if (!isfinite(gap)) goto finish;
    }
    if (valid)
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==2)
        {
            *touched|=ball_touched[i];
            collection_ball_settle(p,&p->collection[i],dt,ball_touched[i],normal_start[i],tangent_start[i]);
            p->ball=p->collection[i].ball;
            if (!collision_ball_valid(p)) { valid=false; break; }
        }
finish:
    p->ball=saved; p->ball_dango_contact=saved_dango; p->ball_floor_contact=saved_floor;
    return valid && collection_refresh(p);
}

static void collection_probe(collision_review* p,int kind,uint64_t target,uint64_t revision,
    const dango_furniture_definition* candidate,dango_furniture_collection_probe* result,int* slot)
{
    *result=(dango_furniture_collection_probe){.world_generation=p->world_generation,.scene_revision=p->scene_revision,
        .physics_step=p->physics_step,.minimum_gap=1024}; *slot=-1;
    if (p->error) { result->rejection=8; return; }
    if (p->count!=1 || p->platform_enabled || p->ball.state.enabled) { result->rejection=2; return; }
    if (revision!=p->scene_revision) { result->rejection=3; return; }
    if (kind<1 || kind>3 || (kind==1 && target!=0) || (kind!=1 && target==0)) { result->rejection=4; return; }
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i)
        if ((kind==1 && !p->collection[i].instance) || (kind!=1 && p->collection[i].instance==target)) { *slot=i; break; }
    if (kind==1 && p->collection_count>=DANGO_FURNITURE_COLLECTION_MAX) { result->rejection=9; return; }
    if (*slot<0) { result->rejection=4; return; }
    if (kind==3) return;
    if (!collection_definition_valid(candidate) || (kind==2 && candidate->kind!=p->collection[*slot].definition.kind))
    { result->rejection=1; return; }
    if (!p->horizontal_bounds_enabled) { result->rejection=5; return; }
    double left,right,bottom,top;
    if (candidate->kind==1)
    { left=candidate->platform.x-candidate->platform.width*.5; right=candidate->platform.x+candidate->platform.width*.5;
      bottom=candidate->platform.y-candidate->platform.height*.5; top=candidate->platform.y+candidate->platform.height*.5; }
    else
    { left=(candidate->ball.x-candidate->ball.radius)*.75; right=(candidate->ball.x+candidate->ball.radius)*.75;
      bottom=candidate->ball.y-candidate->ball.radius; top=candidate->ball.y+candidate->ball.radius; }
    if (left<p->horizontal_left-1e-6 || right>p->horizontal_right+1e-6 || bottom<p->config.ground_height-1e-6 || top>p->config.ground_height+6+1e-6)
    { result->rejection=6; return; }
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        double gap;
        if (candidate->kind==1)
        { display_mesh mesh; if (!display_build(p->bodies[r],p->body_config[r].node_radius,&mesh)) { result->rejection=8; return; }
          gap=platform_separation(&mesh,&candidate->platform).gap; }
        else
        { dango_ball_geometry geometry; dango_ball_geometry_result contact;
          collision_ball_geometry(p,r,candidate->ball.x,candidate->ball.y,candidate->ball.radius,&geometry);
          if (!dango_ball_geometry_contact(&geometry,&contact)) { result->rejection=8; return; } gap=contact.gap; }
        result->minimum_gap=fmin(result->minimum_gap,gap);
        if (gap<-1e-6) { result->rejection=7; return; }
        /* Validate original birth contour as well as the current body. */
        dango_ball_geometry birth=p->ball_birth_geometry[r];
        if (candidate->kind==2)
        { dango_ball_geometry_result contact; birth.ball_x=candidate->ball.x; birth.ball_y=candidate->ball.y; birth.ball_radius=candidate->ball.radius;
          if (!dango_ball_geometry_contact(&birth,&contact)) { result->rejection=8; return; }
          if (contact.gap<-1e-6) { result->rejection=7; return; } }
        else
        {
            dango_ball_mesh birth_mesh;
            if (!dango_ball_mesh_build(&birth,&birth_mesh)) { result->rejection=8; return; }
            display_mesh mesh={0};
            for (int sample=0;sample<DISPLAY_SAMPLES;++sample)
                mesh.points[sample]=(b2Vec2){(float)(birth_mesh.points[sample].x*.75),(float)birth_mesh.points[sample].y};
            if (platform_separation(&mesh,&candidate->platform).gap<-1e-6) { result->rejection=7; return; }
        }
    }
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (i!=*slot && p->collection[i].instance)
    {
        double gap=collection_definition_gap(candidate,&p->collection[i].definition);
        result->minimum_gap=fmin(result->minimum_gap,gap);
        if (gap<-1e-6) { result->rejection=7; return; }
        if (p->collection[i].definition.kind==2)
        {
            dango_furniture_definition actual=p->collection[i].definition;
            actual.ball.x=p->collection[i].ball.state.x; actual.ball.y=p->collection[i].ball.state.y;
            gap=collection_definition_gap(candidate,&actual); result->minimum_gap=fmin(result->minimum_gap,gap);
            if (gap<-1e-6) { result->rejection=7; return; }
        }
    }
}

static int collection_reset(collision_review* p)
{
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i) if (p->collection[i].instance && p->collection[i].definition.kind==2)
    {
        dango_collection_item* item=&p->collection[i];
        item->ball=(dango_native_ball){.definition=item->definition.ball,
            .state={.id=(int)item->instance,.enabled=1,.x=item->definition.ball.x,.y=item->definition.ball.y}};
        memset(item->platform_touch,0,sizeof item->platform_touch); item->dango_contact=item->surface_contact=(dango_ball_contact){0};
    }
    memset(p->collection_pair_touch,0,sizeof p->collection_pair_touch);
    p->collection_prepared=false; p->error=0; p->contact_steps=0;
    int touched=0; if (!collection_substep(p,0,&touched)) { p->error=-403; return p->error; }
    float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0}; p->error=collision_read(p,candidate);
    if (!p->error) { memcpy(p->snapshot,candidate,sizeof candidate); ++p->scene_revision; collision_ball_publish(p); }
    return p->error;
}

DANGO_API int dango_physics_get_furniture_collection_abi_version(void) { return DANGO_FURNITURE_COLLECTION_ABI; }
DANGO_API int dango_physics_get_furniture_collection_frame_size(void) { return sizeof(dango_furniture_collection_frame); }
DANGO_API int dango_physics_world_probe_furniture_change(void* handle,int kind,uint64_t target,uint64_t revision,
    const dango_furniture_definition* candidate,dango_furniture_collection_probe* result,int size)
{
    collision_review* p=handle; if (!p || !result || size!=sizeof *result) return -410;
    int slot; collection_probe(p,kind,target,revision,candidate,result,&slot); return 0;
}
DANGO_API int dango_physics_world_prepare_furniture_change(void* handle,int kind,uint64_t target,uint64_t revision,
    const dango_furniture_definition* candidate,dango_furniture_collection_probe* result,int size,dango_furniture_token* token)
{
    collision_review* p=handle; if (!p || !result || !token || size!=sizeof *result) return -410;
    *token=(dango_furniture_token){0}; int slot; collection_probe(p,kind,target,revision,candidate,result,&slot);
    if (result->rejection) return 0;
    if (p->collection_next_instance>=2147483646 || p->next_furniture_token==UINT64_MAX) return -410;
    p->collection_candidate=(dango_collection_item){0};
    if (kind!=3)
    {
        p->collection_candidate.instance=kind==1 ? p->collection_next_instance+1 : target;
        p->collection_candidate.definition=*candidate;
        if (candidate->kind==2) p->collection_candidate.ball=(dango_native_ball){.definition=candidate->ball,
            .state={.id=(int)p->collection_candidate.instance,.enabled=1,.x=candidate->ball.x,.y=candidate->ball.y}};
    }
    p->collection_prepared_kind=kind; p->collection_prepared_slot=slot;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        display_mesh mesh; if (!display_build(p->bodies[r],p->body_config[r].node_radius,&mesh)) return -410;
        bool grounded=collision_platform_floor(p,p->bodies[r]);
        b2Vec2 core=b2Body_GetPosition(p->bodies[r]->core_body_id),velocity=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id);
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i)
        {
            const dango_collection_item* item=i==slot ? &p->collection_candidate : &p->collection[i];
            if (!item->instance || item->definition.kind!=1) continue;
            dango_platform_contact contact=platform_separation(&mesh,&item->definition.platform);
            if (!isfinite(contact.gap)) return -410;
            grounded=grounded || platform_support_eligible(&contact,core.y,velocity.y,&item->definition.platform);
        }
        p->collection_prepared_grounded[r]=grounded;
    }
    p->collection_token=(dango_furniture_token){.world_generation=p->world_generation,.token_id=++p->next_furniture_token,
        .scene_revision=p->scene_revision,.physics_step=p->physics_step};
    p->collection_prepared=true; *token=p->collection_token; return 0;
}
DANGO_API int dango_physics_world_apply_prepared_furniture_change(void* handle,const dango_furniture_token* token)
{
    collision_review* p=handle;
    if (!p || !token || !p->collection_prepared || p->error ||
        memcmp(token,&p->collection_token,sizeof *token)!=0 || token->scene_revision!=p->scene_revision || token->physics_step!=p->physics_step) return -410;
    int slot=p->collection_prepared_slot;
    p->collection[slot]=p->collection_candidate;
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i)
    { p->collection_pair_touch[i][slot]=p->collection_pair_touch[slot][i]=false; p->collection[i].platform_touch[slot]=false; }
    if (p->collection_prepared_kind==1) { ++p->collection_count; ++p->collection_next_instance; }
    if (p->collection_prepared_kind==3) --p->collection_count;
    p->collection_mode=true; p->collection_prepared=false;
    p->last_applied_furniture_token=token->token_id; ++p->scene_revision;
    /* Validated metadata-only apply cannot move the rig or other balls. */
    for (int r=0;r<p->capacity;++r) if (p->active[r]) p->bodies[r]->grounded=p->collection_prepared_grounded[r];
    collision_ball_publish(p); return 0;
}
DANGO_API int dango_physics_world_cancel_prepared_furniture_change(void* handle,const dango_furniture_token* token)
{
    collision_review* p=handle; if (!p || !token || token->world_generation!=p->world_generation) return -410;
    if (p->collection_prepared && token->token_id==p->collection_token.token_id) p->collection_prepared=false;
    return 0;
}
DANGO_API int dango_physics_world_get_furniture_collection_frame(void* handle,dango_furniture_collection_frame* frame,int size)
{
    collision_review* p=handle; if (!p || !frame || size!=sizeof *frame) return -410;
    if (p->platform_enabled || p->ball.state.enabled) return -410;
    if (p->collection_mode) *frame=p->collection_snapshot;
    else { memset(frame,0,sizeof *frame); collision_furniture_read_core(p,&frame->bodies); }
    frame->bodies.error_code=p->error; return 0;
}

#ifdef DANGO_FURNITURE_COLLECTION_CONTRACT_TESTS
#include <stdio.h>
static int collection_test_checks,collection_test_failures;
static void collection_test_check(bool condition,const char* label)
{
    ++collection_test_checks;
    if (!condition) { ++collection_test_failures; printf("FAIL: %s\n",label); }
}
static dango_self_physics_config collection_test_config(void)
{
    return (dango_self_physics_config){
        .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
        .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=5.2f,.perimeter_hertz=20.1f,
        .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.ground_deformation_strength=3.5f,
        .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,.core_exclusion_ratio=.25f,
        .angle_strength=10,.local_angle_balance_strength=1.7f,.top_inward_force=.5f,.gravity=9.8f,.core_mass=.12f,.node_mass=.015f,
        .drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,.contact_hertz=28,
        .linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
        .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,
        .jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f};
}
static bool collection_test_insert(collision_review* p,dango_furniture_definition definition)
{
    dango_furniture_collection_probe probe; dango_furniture_token token;
    int code=dango_physics_world_prepare_furniture_change(p,1,0,p->scene_revision,&definition,&probe,sizeof probe,&token);
    collection_test_check(code==0 && probe.rejection==0,"real insert preparation admitted");
    if (code || probe.rejection) return false;
    code=dango_physics_world_apply_prepared_furniture_change(p,&token);
    collection_test_check(code==0,"real insert applied"); return code==0;
}
static void collection_test_elevated_damping(dango_self_physics_config* config)
{
    for (int mode=0;mode<2;++mode)
    {
        void* handle=NULL; int id=0,state_id,count,contacts,error;
        float controls[6]={0},states[42];
        dango_physics_world_create(config,1,&handle); collision_review* p=handle;
        dango_physics_world_set_horizontal_bounds(p,-8,8); dango_physics_world_add(p,0,2.5f,&id);
        dango_static_platform platform={0,1,4,.3f,.36f};
        if (mode) collection_test_insert(p,(dango_furniture_definition){.kind=1,.platform=platform});
        else collection_test_check(dango_physics_world_set_static_platform(p,0,1,4,.3f,.36f)==0,"legacy damping reference admitted");
        for (int step=0;step<300;++step)
            dango_physics_world_step(p,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error);
        display_mesh mesh; display_build(p->bodies[0],config->node_radius,&mesh);
        dango_platform_contact contact=platform_separation(&mesh,&platform);
        collection_test_check(!p->error && p->bodies[0]->grounded && contact.normal.y>.7f && fabsf(contact.gap)<.008f,
            "damping fixture has real elevated top support");
        float mass=0; b2Vec2 center={0};
        for (int n=0;n<17;++n)
        {
            b2BodyId body=display_id(p->bodies[0],n); float m=b2Body_GetMass(body);
            mass+=m; center=b2MulAdd(center,m,b2Body_GetPosition(body));
            if (n<16) collection_test_check(b2Body_GetPosition(body).y-config->node_radius>config->ground_height+.1f,
                "damping fixture does not touch real floor");
        }
        center=b2MulSV(1/mass,center);
        for (int n=0;n<17;++n)
        {
            b2BodyId body=display_id(p->bodies[0],n); b2Vec2 offset=b2Sub(b2Body_GetPosition(body),center);
            b2Body_SetLinearVelocity(body,(b2Vec2){2-1.5f*offset.y,1.5f*offset.x});
        }
        const float dt=1.0f/240; collision_damp(p,0,dt,controls);
        b2Vec2 translation={0}; float inertia=0,momentum=0;
        for (int n=0;n<17;++n)
        {
            b2BodyId body=display_id(p->bodies[0],n); float m=b2Body_GetMass(body);
            translation=b2MulAdd(translation,m,b2Body_GetLinearVelocity(body));
        }
        translation=b2MulSV(1/mass,translation);
        for (int n=0;n<17;++n)
        {
            b2BodyId body=display_id(p->bodies[0],n); float m=b2Body_GetMass(body);
            b2Vec2 offset=b2Sub(b2Body_GetPosition(body),center);
            inertia+=m*b2Dot(offset,offset);
            momentum+=m*b2Cross(offset,b2Sub(b2Body_GetLinearVelocity(body),translation));
        }
        float accepted=(2-35*(platform.friction/.72f)*dt)*expf(-60*dt);
        collection_test_check(fabsf(translation.x-accepted)<1e-5f,"elevated support uses accepted finite dry friction and platform coefficient");
        collection_test_check(fabsf(momentum/inertia)<1.1f,"elevated support applies accepted passive spin damping");
        dango_physics_world_destroy(p);
    }
}
int dango_furniture_collection_contract_tests(void)
{
    dango_self_physics_config config=collection_test_config(); void* handle=NULL; int id=0,initial=b2GetByteCount();
    collection_test_elevated_damping(&config);
    collection_test_check(dango_physics_world_create(&config,1,&handle)==0,"create owned world");
    if (!handle) return 1;
    collision_review* p=handle;
    collection_test_check(dango_physics_world_set_horizontal_bounds(p,-15,15)==0,"room bounds");
    collection_test_check(dango_physics_world_add(p,-13,0,&id)==0,"one original rig");
    for (int i=0;i<30;++i)
    {
        dango_furniture_definition d={.kind=1,.platform={-10.0f+(float)(i%10)*2,1.0f+(float)(i/10),.5f,.2f,.72f}};
        if (!collection_test_insert(p,d)) break;
    }
    collection_test_check(p->collection_count==30,"native collection accepts thirty distinct items");
    dango_furniture_collection_frame before,after; dango_furniture_collection_probe probe; dango_furniture_token token;
    dango_physics_world_get_furniture_collection_frame(p,&before,sizeof before);
    dango_furniture_definition extra={.kind=1,.platform={11,1,.5f,.2f,.72f}};
    collection_test_check(dango_physics_world_prepare_furniture_change(p,1,0,p->scene_revision,&extra,&probe,sizeof probe,&token)==0 &&
        probe.rejection==9 && token.token_id==0,"thirty-first item rejected without token");
    dango_physics_world_get_furniture_collection_frame(p,&after,sizeof after);
    collection_test_check(memcmp(&before,&after,sizeof before)==0,"capacity rejection preserves complete frame");
    collection_test_check(dango_physics_world_get_furniture_frame(p,&after.bodies,sizeof after.bodies)!=0,"old ABI cannot truncate collection");
    memset(&after,0xCD,sizeof after); dango_furniture_collection_frame sentinel=after;
    collection_test_check(dango_physics_world_get_furniture_collection_frame(p,&after,sizeof after-1)!=0 &&
        memcmp(&after,&sentinel,sizeof after)==0,"exact size checked before any output write");
    collection_test_check(dango_physics_world_destroy(p)==0,"capacity world release");
    handle=NULL; dango_physics_world_create(&config,1,&handle); p=handle;
    dango_physics_world_set_horizontal_bounds(p,-8,8); dango_physics_world_add(p,-6,0,&id);
    dango_furniture_definition platform={.kind=1,.platform={2,.5f,3.4f,.35f,.72f}};
    dango_furniture_definition ball={.kind=2,.ball={.x=8.0/3,.y=3,.radius=1.1,.mass=.02,
        .floor_friction=.45,.dango_friction=.35,.floor_restitution=.25,.dango_restitution=.15}};
    collection_test_insert(p,platform); collection_test_insert(p,ball);
    int state_id,count,contacts,error; float controls[6]={0},states[42];
    for (int step=0;step<600;++step)
    {
        int code=dango_physics_world_step(p,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error);
        collection_test_check(code==0 && error==0,"ball-platform joint solver stays healthy");
        if (error) break;
    }
    dango_physics_world_get_furniture_collection_frame(p,&before,sizeof before);
    collection_test_check(before.items[1].ball.y>=.675+1.1-.001,"ball is supported by physical platform");
    dango_physics_world_prepare_furniture_change(p,3,before.items[0].instance,p->scene_revision,NULL,&probe,sizeof probe,&token);
    collection_test_check(probe.rejection==0,"delete supported platform admitted");
    collection_test_check(dango_physics_world_apply_prepared_furniture_change(p,&token)==0,"delete applies once");
    dango_physics_world_get_furniture_collection_frame(p,&after,sizeof after);
    collection_test_check(memcmp(&before.items[1].ball.x,&after.items[0].ball.x,6*sizeof(double))==0,"delete preserves ball kinematics");
    for (int step=0;step<180;++step) dango_physics_world_step(p,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error);
    collection_test_check(!p->error && p->collection[1].ball.state.y<.5,"unsupported ball really falls");
    dango_physics_world_destroy(p);
    handle=NULL; dango_physics_world_create(&config,1,&handle); p=handle;
    dango_physics_world_set_horizontal_bounds(p,-8,8); dango_physics_world_add(p,-6,0,&id);
    ball.ball.x=-1.2; ball.ball.y=.38; collection_test_insert(p,ball);
    ball.ball.x=1.2; collection_test_insert(p,ball);
    p->collection[0].ball.state.vx=1.5;
    double max_right=0;
    for (int step=0;step<180;++step)
    {
        dango_physics_world_step(p,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error);
        collection_test_check(!error,"two-ball contact remains healthy");
        max_right=fmax(max_right,p->collection[1].ball.state.vx);
        if (error) break;
    }
    collection_test_check(max_right>.3,"first ball transfers real momentum to second ball");
    dango_physics_world_get_furniture_collection_frame(p,&before,sizeof before);
    p->collection[0].ball.state.vx=NAN;
    dango_physics_world_step(p,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error);
    dango_physics_world_get_furniture_collection_frame(p,&after,sizeof after);
    collection_test_check(error!=0,"real nonfinite fault latches");
    after.bodies.error_code=before.bodies.error_code;
    collection_test_check(memcmp(&before,&after,sizeof before)==0,"fault freezes full body, diagnostics and every furniture item");
    dango_physics_world_destroy(p);
    for (int cycle=0;cycle<100;++cycle)
    {
        handle=NULL; dango_physics_world_create(&config,1,&handle); p=handle;
        dango_physics_world_set_horizontal_bounds(p,-8,8); dango_physics_world_add(p,-6,0,&id);
        ball.ball.x=2; ball.ball.y=3; collection_test_insert(p,ball);
        dango_physics_world_get_furniture_collection_frame(p,&before,sizeof before);
        dango_physics_world_prepare_furniture_change(p,3,before.items[0].instance,p->scene_revision,NULL,&probe,sizeof probe,&token);
        collection_test_check(dango_physics_world_apply_prepared_furniture_change(p,&token)==0,"resource loop delete");
        collection_test_check(dango_physics_world_reset(p)==0,"empty collection reset");
        collection_test_check(dango_physics_world_destroy(p)==0,"resource loop release");
        collection_test_check(b2GetByteCount()==initial,"resource loop native allocation returns to baseline");
    }
    collection_test_check(b2GetByteCount()==initial,"all worlds and native allocations released");
    printf("Furniture collection: %d checks, %d failures\n",collection_test_checks,collection_test_failures);
    return collection_test_failures;
}
#endif

#endif

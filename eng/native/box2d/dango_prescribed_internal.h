#ifndef DANGO_PRESCRIBED_INTERNAL_H
#define DANGO_PRESCRIBED_INTERNAL_H
#include <float.h>
#include "dango_self_constraints.h"
#ifndef DANGO_PRESCRIBED_TEST_VARIANT
#define DANGO_PRESCRIBED_TEST_VARIANT 0
#endif
_Static_assert(sizeof(dango_prescribed_body)==56,"prescribed body ABI");
_Static_assert(sizeof(dango_prescribed_step)==248,"prescribed input ABI");
_Static_assert(sizeof(dango_prescribed_trace)==3936,"prescribed trace ABI");

static float prescribed_dt(void)
{
    uint32_t bits=UINT32_C(0x3C888865); float dt; memcpy(&dt,&bits,sizeof dt); return dt;
}
static void prescribed_curve(const dango_prescribed_body* b,double u,b2Vec2* position,b2Vec2* velocity)
{
    double t=prescribed_dt(),u2=u*u,u3=u2*u;
    const float start[2]={b->start_x,b->start_y},end[2]={b->end_x,b->end_y};
    const float sv[2]={b->start_vx,b->start_vy},ev[2]={b->end_vx,b->end_vy};
    float p[2],v[2];
    for (int axis=0;axis<2;++axis)
    {
        p[axis]=(float)((2*u3-3*u2+1)*start[axis]+(u3-2*u2+u)*t*sv[axis]+
            (-2*u3+3*u2)*end[axis]+(u3-u2)*t*ev[axis]);
        v[axis]=(float)(((6*u2-6*u)*start[axis]+(3*u2-4*u+1)*t*sv[axis]+
            (-6*u2+6*u)*end[axis]+(3*u2-2*u)*t*ev[axis])/t);
    }
    *position=(b2Vec2){p[0],p[1]}; *velocity=(b2Vec2){v[0],v[1]};
}
static bool prescribed_constraints(dango_self_soft_body* body,const dango_self_physics_config* config)
{
    dango_constraint_state s={.radial_length=body->radial_rest_length,.perimeter_length=body->perimeter_rest_length,
        .radial_min=config->radial_minimum_ratio,.radial_max=config->radial_maximum_ratio,
        .perimeter_min=config->perimeter_minimum_ratio,.perimeter_max=config->perimeter_maximum_ratio,
        .radius=config->node_radius,.ground=config->ground_height};
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(body,n);
        s.positions[n]=b2Body_GetPosition(id); s.velocities[n]=b2Body_GetLinearVelocity(id);
        s.inverse_mass[n]=n==16 ? 0 : 1/b2Body_GetMass(id);
    }
    if (!dango_constraint_solve(&s)) return false;
    for (int n=0;n<16;++n)
    {
        b2BodyId id=body->node_body_ids[n];
        b2Body_SetTransform(id,s.positions[n],b2Rot_identity); b2Body_SetLinearVelocity(id,s.velocities[n]);
    }
    return true;
}
static bool prescribed_monitor(collision_review* p,const dango_prescribed_step* input,const int* slots,
    double fraction,dango_prescribed_trace* trace,bool record,int index)
{
    bool valid=true;
    for (int b=0;b<input->body_count;++b)
    {
        b2Vec2 target,velocity; prescribed_curve(&input->bodies[b],fraction,&target,&velocity);
        b2Vec2 actual=b2Body_GetPosition(p->bodies[slots[b]]->core_body_id);
        float error=fmaxf(fabsf(actual.x-target.x),fabsf(actual.y-target.y));
        trace->maximum_core_errors[b]=fmaxf(trace->maximum_core_errors[b],error);
        if (!b2IsValidVec2(actual) || !isfinite(error) || error>1e-5f) valid=false;
        if (record) trace->samples[b][index]=(dango_prescribed_sample){(float)fraction,actual.x,actual.y,target.x,target.y};
    }
    return valid;
}
static int prescribed_body_index(const dango_prescribed_step* input,int id)
{
    for (int i=0;i<input->body_count;++i) if (input->bodies[i].entity_id==id) return i;
    return -1;
}
static bool prescribed_semantics_valid(const collision_review* p,const dango_prescribed_step* input,bool end)
{
    int count=end ? input->end_pair_count : input->start_pair_count;
    const dango_authority_pair* pair=end ? input->end_pairs : input->start_pairs;
    dango_authority_pair zero={0};
    if (count<0 || count>1 || (count==0 && memcmp(pair,&zero,sizeof zero)!=0)) return false;
    for (int b=0;b<input->body_count;++b)
    {
        const dango_prescribed_body* body=&input->bodies[b];
        int support=end ? body->end_support : body->start_support;
        if (!support) continue;
        if (support==body->entity_id || prescribed_body_index(input,support)<0 || count!=1) return false;
        bool a=pair->entity_a==body->entity_id && pair->entity_b==support && (pair->flags&2)!=0;
        bool other=pair->entity_b==body->entity_id && pair->entity_a==support && (pair->flags&4)!=0;
        if (!a && !other) return false;
    }
    if (!count) return true;
    int a=prescribed_body_index(input,pair->entity_a),b=prescribed_body_index(input,pair->entity_b);
    if (a<0 || b<0 || a==b || pair->reserved || (pair->flags&~7u) || !(pair->flags&1) ||
        !isfinite(pair->nx) || !isfinite(pair->ny) || !isfinite(pair->gap) || !isfinite(pair->minimum_projected_width) ||
        fabsf(pair->nx*pair->nx+pair->ny*pair->ny-1)>.0002f || pair->gap>.008f || pair->minimum_projected_width<=0 ||
        (pair->flags&6)==6) return false;
    const dango_prescribed_body *ba=&input->bodies[a],*bb=&input->bodies[b];
    b2Vec2 pa=end ? (b2Vec2){ba->end_x,ba->end_y} : (b2Vec2){ba->start_x,ba->start_y};
    b2Vec2 pb=end ? (b2Vec2){bb->end_x,bb->end_y} : (b2Vec2){bb->start_x,bb->start_y};
    float reach=2*(p->config.radial_rest_length*p->config.radial_maximum_ratio+p->config.node_radius)+.008f;
    if (b2Distance(pa,pb)>reach || b2Dot(b2Sub(pb,pa),(b2Vec2){pair->nx,pair->ny})<=0) return false;
    int sa=end ? ba->end_support : ba->start_support,sb=end ? bb->end_support : bb->start_support;
    if (((pair->flags&2)!=0)!=(sa==bb->entity_id) || ((pair->flags&4)!=0)!=(sb==ba->entity_id)) return false;
    if ((pair->flags&2) && (-pair->ny<.7f || pa.y<=pb.y+.4f)) return false;
    if ((pair->flags&4) && (pair->ny<.7f || pb.y<=pa.y+.4f)) return false;
    return true;
}
static int prescribed_validate(collision_review* p,const dango_prescribed_step* input,int* slots,int small_steps)
{
    if (input->abi_version!=1 || input->frame_size!=248 || input->reserved || input->body_count!=p->count ||
        input->body_count<1 || input->body_count>2 || !isfinite(input->start_time) || !isfinite(input->end_time) ||
        fabs(input->end_time-input->start_time-1)>1e-9 || !input->start_revision ||
        input->end_revision<input->start_revision || !input->ordinal || small_steps<1 || small_steps>96) return -410;
    if (input->epoch!=p->prescribed_epoch) return DANGO_AUTHORITY_MODE_CONFLICT;
    if (p->prescribed_previous.ordinal==UINT64_MAX || input->ordinal!=p->prescribed_previous.ordinal+1 ||
        (p->prescribed_previous.ordinal && (input->start_time!=p->prescribed_previous.end_time ||
        input->start_revision!=p->prescribed_previous.end_revision))) return -410;
    dango_prescribed_body zero={0};
    for (int b=input->body_count;b<2;++b) if (memcmp(&input->bodies[b],&zero,sizeof zero)!=0) return -410;
    bool seen[3]={false};
    for (int b=0;b<input->body_count;++b)
    {
        const dango_prescribed_body* body=&input->bodies[b];
        if (body->reserved || body->start_floor>1 || body->end_floor>1 || body->start_support<0 || body->end_support<0) return -410;
        int slot=-1;
        for (int s=0;s<p->capacity;++s) if (p->active[s] && p->entity_ids[s]==body->entity_id) slot=s;
        if (slot<0 || seen[slot]) return -412;
        seen[slot]=true; slots[b]=slot;
        for (int k=0;k<=small_steps;++k)
        {
            b2Vec2 target,velocity; prescribed_curve(body,(double)k/small_steps,&target,&velocity);
            if (!b2IsValidVec2(target) || !b2IsValidVec2(velocity) || fabsf(target.x)>1024 || fabsf(target.y)>1024 ||
                b2Length(velocity)>p->config.maximum_speed) return -410;
        }
        if (p->prescribed_initialized[slot])
        {
            int previous=prescribed_body_index(&p->prescribed_previous,body->entity_id);
            if (previous<0) return -410;
            const dango_prescribed_body* old=&p->prescribed_previous.bodies[previous];
            int support=prescribed_body_index(input,old->end_support)>=0 ? old->end_support : 0;
            if (body->start_floor!=old->end_floor || body->start_support!=support) return -410;
            b2Vec2 actual=b2Body_GetPosition(p->bodies[slot]->core_body_id),v=b2Body_GetLinearVelocity(p->bodies[slot]->core_body_id);
            if (fabsf(actual.x-body->start_x)>1e-5f || fabsf(actual.y-body->start_y)>1e-5f ||
                fabsf(v.x-body->start_vx)>1e-5f || fabsf(v.y-body->start_vy)>1e-5f) return -410;
        }
        else
        {
            if (body->start_floor || body->start_support || body->start_vx!=body->end_vx || body->start_vy!=body->end_vy ||
                fabs((double)body->start_x-((double)body->end_x-body->end_vx*prescribed_dt()))>1e-5 ||
                fabs((double)body->start_y-((double)body->end_y-body->end_vy*prescribed_dt()))>1e-5) return -410;
            b2Vec2 delta=b2Sub((b2Vec2){body->start_x,body->start_y},b2Body_GetPosition(p->bodies[slot]->core_body_id));
            for (int n=0;n<16;++n)
                if (!b2IsValidVec2(b2Add(b2Body_GetPosition(p->bodies[slot]->node_body_ids[n]),delta))) return -410;
        }
    }
    dango_authority_pair expected={0}; int expected_count=0;
    if (p->prescribed_previous.end_pair_count)
    {
        const dango_authority_pair* previous=&p->prescribed_previous.end_pairs[0];
        if (prescribed_body_index(input,previous->entity_a)>=0 && prescribed_body_index(input,previous->entity_b)>=0)
        { expected=*previous; expected_count=1; }
    }
    if (input->start_pair_count!=expected_count || memcmp(input->start_pairs,&expected,sizeof expected)!=0) return -410;
    return prescribed_semantics_valid(p,input,false) && prescribed_semantics_valid(p,input,true) ? 0 : -410;
}
static bool prescribed_contact(collision_review* p,const int* slots)
{
    dango_self_soft_body* bodies[2]={p->bodies[slots[0]],p->bodies[slots[1]]};
    for (int iteration=0;iteration<96;++iteration)
    {
        display_mesh m[2]; b2Vec2 normal; int ia,ib;
        if (!display_build(bodies[0],p->config.node_radius,&m[0]) || !display_build(bodies[1],p->config.node_radius,&m[1])) return false;
        float gap=display_separation(&m[0],&m[1],&normal,&ia,&ib,.001f);
        float margin=display_contact_margin(&m[0],&m[1],normal,true);
        if (!isfinite(gap)) return false;
        if (gap>=margin-1e-5f) return true;
        b2Vec2 g[2][17]; display_gradient(&m[0],ia,b2Neg(normal),g[0]); display_gradient(&m[1],ib,normal,g[1]);
        double k=0,vn=0,c=0,absolute_rate=0,before=0;
        for (int b=0;b<2;++b)
        {
            b2Vec2 cv=b2Body_GetLinearVelocity(bodies[b]->core_body_id);
            c+=(double)g[b][16].x*cv.x+(double)g[b][16].y*cv.y;
            for (int n=0;n<16;++n)
            {
                b2BodyId id=bodies[b]->node_body_ids[n]; double mass=b2Body_GetMass(id);
                b2Vec2 v=b2Body_GetLinearVelocity(id);
                double rate=(double)g[b][n].x*v.x+(double)g[b][n].y*v.y;
                vn+=rate; absolute_rate+=fabs(rate);
                k+=((double)g[b][n].x*g[b][n].x+(double)g[b][n].y*g[b][n].y)/mass;
                before+=.5*mass*((double)v.x*v.x+(double)v.y*v.y);
            }
        }
        vn+=c;
        if (!isfinite(k) || k<=1e-12 || !isfinite(vn)) return false;
        double correction=fmin((double)margin-gap,.01)/k,lambda=fmax(0,-vn/k),work=-lambda*c;
        double after=0,star_energy=0,r32=0;
        for (int b=0;b<2;++b) for (int n=0;n<16;++n)
        {
            b2BodyId id=bodies[b]->node_body_ids[n]; double mass=b2Body_GetMass(id);
            b2Vec2 old=b2Body_GetLinearVelocity(id),position=b2Body_GetPosition(id);
            double vx=(double)old.x+lambda*g[b][n].x/mass,vy=(double)old.y+lambda*g[b][n].y/mass;
            b2Vec2 next={(float)vx,(float)vy};
#if DANGO_PRESCRIBED_TEST_VARIANT == 2
            /* Executable-only counterexample: actual extra node energy, outside
               the prescribed projection and its precomputed rounding bound. */
            next.x+=1.0f;
#endif
            b2Vec2 moved={(float)((double)position.x+correction*g[b][n].x/mass),
                (float)((double)position.y+correction*g[b][n].y/mass)};
            if (!b2IsValidVec2(next) || !b2IsValidVec2(moved)) return false;
            double rx=0x1p-24*fabs(vx)+FLT_TRUE_MIN,ry=0x1p-24*fabs(vy)+FLT_TRUE_MIN;
            r32+=mass*(fabs(vx)*rx+.5*rx*rx+fabs(vy)*ry+.5*ry*ry);
            star_energy+=.5*mass*(vx*vx+vy*vy); after+=.5*mass*((double)next.x*next.x+(double)next.y*next.y);
            b2Body_SetTransform(id,moved,b2Rot_identity); b2Body_SetLinearVelocity(id,next);
        }
        double gamma=512*0x1p-53/(1-512*0x1p-53);
        double r64=gamma*(before+star_energy+fabs(work)+fabs(lambda)*absolute_rate+lambda*lambda*k);
#ifdef DANGO_PRESCRIBED_CONTRACT_TESTS
        ++p->prescribed_energy_checks;
        p->prescribed_last_work=work; p->prescribed_last_residual=after-before-work;
        p->prescribed_last_r32=r32; p->prescribed_last_r64=r64;
        p->prescribed_total_work+=work; p->prescribed_max_abs_work=fmax(p->prescribed_max_abs_work,fabs(work));
        p->prescribed_max_energy_excess=fmax(p->prescribed_max_energy_excess,after-before-work-1e-6-r32-r64);
#endif
        if (!isfinite(after) || !isfinite(work) || after-before-work>1e-6+r32+r64) return false;
        for (int b=0;b<2;++b) if (!prescribed_constraints(bodies[b],&p->config)) return false;
        ++p->contact_steps;
    }
    return false;
}
static bool prescribed_node_damping(dango_self_soft_body* body,float dt,bool grounded)
{
    b2Vec2 positions[16],v[16],center={0},translation={0}; float masses[16],mass=0,inertia=0,momentum=0;
    for (int n=0;n<16;++n)
    {
        b2BodyId id=body->node_body_ids[n]; positions[n]=b2Body_GetPosition(id); v[n]=b2Body_GetLinearVelocity(id);
        masses[n]=b2Body_GetMass(id); mass+=masses[n];
        center=b2MulAdd(center,masses[n],positions[n]); translation=b2MulAdd(translation,masses[n],v[n]);
    }
    if (grounded)
    {
        center=b2MulSV(1/mass,center); translation=b2MulSV(1/mass,translation);
        for (int n=0;n<16;++n)
        {
            b2Vec2 r=b2Sub(positions[n],center); inertia+=masses[n]*b2Dot(r,r);
            momentum+=masses[n]*b2Cross(r,b2Sub(v[n],translation));
        }
        if (inertia>=1e-8f)
        {
            float delta=(expf(-120*dt)-1)*momentum/inertia;
            for (int n=0;n<16;++n)
            {
                b2Vec2 r=b2Sub(positions[n],center); v[n]=b2MulAdd(v[n],delta,(b2Vec2){-r.y,r.x});
            }
        }
        float horizontal=0;
        for (int n=0;n<16;++n) horizontal+=masses[n]*v[n].x;
        float delta=b2ClampFloat(horizontal/mass,-35*dt,35*dt);
        for (int n=0;n<16;++n) v[n].x=(v[n].x-delta)*expf(-60*dt);
    }
    for (int n=0;n<16;++n)
    {
        if (!b2IsValidVec2(v[n])) return false;
        b2Body_SetLinearVelocity(body->node_body_ids[n],v[n]);
    }
    return true;
}
static bool prescribed_node_limit(dango_self_soft_body* body,float limit)
{
    float maximum=0;
    for (int n=0;n<16;++n)
    {
        float speed=b2Length(b2Body_GetLinearVelocity(body->node_body_ids[n]));
        if (!isfinite(speed)) return false;
        maximum=fmaxf(maximum,speed);
    }
    if (maximum>limit) for (int n=0;n<16;++n)
        b2Body_SetLinearVelocity(body->node_body_ids[n],b2MulSV(limit*.99999f/maximum,b2Body_GetLinearVelocity(body->node_body_ids[n])));
    return true;
}
DANGO_API int dango_physics_get_prescribed_abi_version(void) { return 1; }
DANGO_API int dango_physics_get_prescribed_step_size(void) { return sizeof(dango_prescribed_step); }
DANGO_API int dango_physics_get_prescribed_trace_size(void) { return sizeof(dango_prescribed_trace); }
DANGO_API int dango_physics_world_bind_prescribed_epoch(void* handle,uint64_t epoch)
{
    collision_review* p=handle;
    if (!p || !epoch || p->physics_step || p->error) return -410;
    if (p->authority_enabled || p->presentation_mode || (p->prescribed_mode && epoch!=p->prescribed_epoch)) return DANGO_AUTHORITY_MODE_CONFLICT;
    if (!authority_role_supported(p)) return DANGO_AUTHORITY_UNSUPPORTED;
    if (p->prescribed_mode) return 0;
    p->prescribed_mode=true; p->prescribed_epoch=epoch;
    p->prescribed_trace=(dango_prescribed_trace){.abi_version=1,.frame_size=3936,.epoch=epoch};
    for (int s=0;s<p->capacity;++s) if (p->active[s]) b2Body_SetType(p->bodies[s]->core_body_id,b2_kinematicBody);
#if DANGO_PRESCRIBED_TEST_VARIANT == 1
    for (int s=0;s<p->capacity;++s) if (p->active[s]) b2Body_SetType(p->bodies[s]->core_body_id,b2_dynamicBody);
#endif
    return 0;
}
DANGO_API int dango_physics_world_step_prescribed(void* handle,const dango_prescribed_step* input,int input_size,
    int* state_ids,float* states,int capacity,int* state_count,int* contact_steps,dango_prescribed_trace* output,int trace_size)
{
    collision_review* p=handle;
    if (!p || !input || input_size!=248 || !state_ids || !states || capacity<p->count*42 || !state_count ||
        !contact_steps || !output || trace_size!=3936) return -410;
    if (!p->prescribed_mode) return DANGO_AUTHORITY_MODE_CONFLICT;
    if (input->epoch!=p->prescribed_epoch) return DANGO_AUTHORITY_MODE_CONFLICT;
    float dt=prescribed_dt(); int small_steps=(int)ceilf(dt*960),slots[2]={0};
    if (small_steps<p->config.substep_count) small_steps=p->config.substep_count;
    if (!p->error)
    {
        int safe_contact_steps=p->contact_steps;
        int invalid=prescribed_validate(p,input,slots,small_steps); if (invalid) return invalid;
        dango_prescribed_trace trace={.abi_version=1,.frame_size=3936,.epoch=input->epoch,.ordinal=input->ordinal,
            .body_count=input->body_count,.sample_count=1};
        for (int b=0;b<input->body_count;++b)
        {
            int s=slots[b]; dango_self_soft_body* body=p->bodies[s]; const dango_prescribed_body* requested=&input->bodies[b];
            trace.entity_ids[b]=requested->entity_id;
            if (!p->prescribed_initialized[s])
            {
                b2Vec2 delta=b2Sub((b2Vec2){requested->start_x,requested->start_y},b2Body_GetPosition(body->core_body_id));
                for (int n=0;n<17;++n)
                {
                    b2BodyId id=display_id(body,n);
                    b2Body_SetTransform(id,b2Add(b2Body_GetPosition(id),delta),b2Rot_identity);
                    b2Body_SetLinearVelocity(id,(b2Vec2){requested->start_vx,requested->start_vy});
                }
                p->prescribed_initialized[s]=true;
            }
        }
        if (!prescribed_monitor(p,input,slots,0,&trace,true,0)) p->error=-422;
        float small_dt=dt/(float)small_steps;
        for (int sub=0;sub<small_steps && !p->error;++sub)
        {
            double u=(double)(sub+1)/small_steps; bool end=sub+1==small_steps;
            for (int b=0;b<input->body_count;++b)
            {
                dango_self_soft_body* body=p->bodies[slots[b]]; const dango_prescribed_body* requested=&input->bodies[b];
                body->grounded=(end ? requested->end_floor : requested->start_floor)!=0;
                for (int n=0;n<16;++n) b2Shape_SetFriction(body->node_shape_ids[n],16);
                dango_self_apply_step_forces(body,&p->config,small_dt,sub,0,0,0,0,0,0);
                int support=end ? requested->end_support : requested->start_support;
                if (!body->grounded && support) { body->grounded=true; dango_self_apply_ground_deformation_force(body,&p->config); body->grounded=false; }
                b2Vec2 target,velocity; prescribed_curve(requested,u,&target,&velocity);
                b2Vec2 current=b2Body_GetPosition(body->core_body_id);
                b2Body_SetLinearVelocity(body->core_body_id,b2MulSV(1/small_dt,b2Sub(target,current)));
            }
            b2World_Step(p->bodies[0]->world_id,small_dt,1);
            trace.substep_count=sub+1; trace.sample_count=sub+2;
            if (!prescribed_monitor(p,input,slots,u,&trace,true,sub+1)) { p->error=-422; break; }
            for (int b=0;b<input->body_count;++b)
            {
                b2Vec2 target,velocity; prescribed_curve(&input->bodies[b],u,&target,&velocity);
                b2Body_SetLinearVelocity(p->bodies[slots[b]]->core_body_id,velocity);
                if (!prescribed_constraints(p->bodies[slots[b]],&p->config)) p->error=-91;
            }
            if (!prescribed_monitor(p,input,slots,u,&trace,true,sub+1)) p->error=-422;
            int pair_count=end ? input->end_pair_count : input->start_pair_count;
            if (!p->error && pair_count && !prescribed_contact(p,slots)) p->error=-422;
            if (!prescribed_monitor(p,input,slots,u,&trace,true,sub+1)) p->error=-422;
            for (int b=0;b<input->body_count && !p->error;++b)
            {
                const dango_prescribed_body* requested=&input->bodies[b];
                bool floor=(end ? requested->end_floor : requested->start_floor)!=0;
                if (!prescribed_node_damping(p->bodies[slots[b]],small_dt,floor) ||
                    !prescribed_node_limit(p->bodies[slots[b]],p->config.maximum_speed)) p->error=-87;
            }
            if (!prescribed_monitor(p,input,slots,u,&trace,true,sub+1)) p->error=-422;
        }
        float candidate[126]={0};
        if (!p->error) p->error=collision_read(p,candidate);
        if (!p->error)
        {
            for (int b=0;b<input->body_count;++b) candidate[slots[b]*42+39]=(float)input->bodies[b].end_floor;
            memcpy(p->snapshot,candidate,sizeof candidate); ++p->physics_step;
            p->prescribed_previous=*input; p->prescribed_trace=trace;
        }
        else { trace.error_code=p->error; p->prescribed_trace=trace; p->contact_steps=safe_contact_steps; }
    }
    int count=0;
    for (int s=0;s<p->capacity;++s) if (p->active[s])
    {
        state_ids[count]=p->entity_ids[s]; memcpy(states+42*count,p->snapshot+42*s,42*sizeof(float)); ++count;
    }
    *state_count=count; *contact_steps=p->contact_steps; *output=p->prescribed_trace; output->error_code=p->error;
    return 0;
}
#endif

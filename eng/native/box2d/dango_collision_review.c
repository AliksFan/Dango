// Isolated shared-world experiment. Does not replace dango_box2d.dll.
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include "dango_self_physics_internal.h"
#include "dango_physics_module.h"
#include "dango_display_contact.h"

typedef struct collision_review
{
    dango_self_soft_body* bodies[DANGO_PHYSICS_MAX_BODIES];
    dango_self_soft_body additional[DANGO_PHYSICS_MAX_BODIES-1];
    dango_self_physics_config config;
    int count, capacity, error, contact_steps, disable_contact_for_test, disable_damping_for_test, legacy_interaction_flags;
    int active[DANGO_PHYSICS_MAX_BODIES], entity_ids[DANGO_PHYSICS_MAX_BODIES], next_entity_id;
    float anchors[DANGO_PHYSICS_MAX_BODIES*2];
    float snapshot[DANGO_PHYSICS_MAX_BODIES*42];
    b2ShapeId horizontal_walls[2];
    bool horizontal_bounds_enabled;
    float horizontal_left, horizontal_right;
    float preview_jump_speed_scale;
} collision_review;

DANGO_API void dango_collision_test_interaction_flags(void* handle,int flags)
{
    if (handle) ((collision_review*)handle)->legacy_interaction_flags=flags&255;
}

static bool collision_support_eligible(float gap,float upward_normal,b2Vec2 upper,b2Vec2 lower,b2Vec2 uv,b2Vec2 lv)
{
    return gap<=.008f && upward_normal>=.7f && upper.y>lower.y+.4f && uv.y-lv.y<.5f;
}

static int collision_support_slot(collision_review* p,int r)
{
    if (p->disable_contact_for_test || (p->legacy_interaction_flags&8)) return -1;
    for (int other=0;other<p->capacity;++other)
    {
        if (other==r || !p->active[other]) continue;
        int a=r<other ? r : other,b=r<other ? other : r;
        display_mesh m[2]; b2Vec2 normal; int ia,ib;
        if (!display_build(p->bodies[a],p->config.node_radius,&m[0]) || !display_build(p->bodies[b],p->config.node_radius,&m[1])) continue;
        float gap=display_separation(&m[0],&m[1],&normal,&ia,&ib,.008f);
        float upward=r==a ? -normal.y : normal.y;
        if (gap>.008f || upward<.7f) continue;
        b2Vec2 upper=b2Body_GetPosition(p->bodies[r]->core_body_id),lower=b2Body_GetPosition(p->bodies[other]->core_body_id);
        b2Vec2 uv=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id),lv=b2Body_GetLinearVelocity(p->bodies[other]->core_body_id);
        if (collision_support_eligible(gap,upward,upper,lower,uv,lv)) return other;
    }
    return -1;
}

static void collision_launch(collision_review* p,int r,bool supported,int other)
{
    float impulse=0,other_mass=0,speed=supported ? 6.0f : 7.7f;
    // A single-body editor may opt into a lower real launch. The default
    // branch retains the accepted arithmetic and every ordinary-world bit.
    if (p->preview_jump_speed_scale!=1.0f) speed*=p->preview_jump_speed_scale;
    b2Vec2 direction={0,1},point={0}; float weights[17]={0},weight_sum=0;
    if (supported)
    {
        float relative=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id).y-b2Body_GetLinearVelocity(p->bodies[other]->core_body_id).y;
        speed=fmaxf(0,speed-relative);
        int a=r<other ? r : other,b=r<other ? other : r;
        display_mesh m[2]; b2Vec2 normal; int ia,ib;
        if (!(p->legacy_interaction_flags&32) && display_build(p->bodies[a],p->config.node_radius,&m[0]) && display_build(p->bodies[b],p->config.node_radius,&m[1]))
        {
            (void)display_separation(&m[0],&m[1],&normal,&ia,&ib,.008f);
            // Contact location distributes support pressure, never launch direction.
            point=r==b ? m[0].points[ia] : m[1].points[ib];
        }
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[other],n); float mass=b2Body_GetMass(id);
            float distance=b2DistanceSquared(b2Body_GetPosition(id),point);
            weights[n]=mass*expf(-distance/(.55f*.55f)); weight_sum+=weights[n]; other_mass+=mass;
        }
    }
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[r],n); float kick=b2Body_GetMass(id)*speed;
        b2Body_ApplyLinearImpulseToCenter(id,b2MulSV(kick,direction),true); impulse+=kick;
    }
    // Equal opposite impulse; the original deformable support absorbs it against the floor.
    if (supported) for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[other],n);
        float fraction=b2Body_GetMass(id)/other_mass;
        if (!(p->legacy_interaction_flags&32) && weight_sum>1e-8f) fraction=.5f*fraction+.5f*weights[n]/weight_sum;
        b2Body_ApplyLinearImpulseToCenter(id,b2MulSV(-impulse*fraction,direction),true);
    }
    p->bodies[r]->grounded=false; p->bodies[r]->seconds_since_jump=0;
}

DANGO_API void dango_collision_disable_damping_for_test(void* handle)
{
    if (handle) ((collision_review*)handle)->disable_damping_for_test=1;
}

DANGO_API int dango_collision_contract_tests(void* handle,int broken)
{
    collision_review* p=handle; if (!p) return -1;
    int failures=0;
    // Analytic unequal ellipses: full-direction, scale-covariant smaller-body rule.
    for (int scale_index=0;scale_index<3;++scale_index) for (int degree=0;degree<360;degree+=5)
    {
        float scale=scale_index==0 ? .5f : scale_index==1 ? 1 : 2;
        float angle=degree*B2_PI/180;
        b2Vec2 normal={cosf(angle),sinf(angle)};
        display_mesh a={0},b={0}; float low=INFINITY,high=-INFINITY;
        for (int i=0;i<DISPLAY_SAMPLES;++i)
        {
            float t=2*B2_PI*i/DISPLAY_SAMPLES;
            b2Vec2 point={scale*cosf(t),.6f*scale*sinf(t)};
            a.points[i]=point; b.points[i]=b2MulSV(1.7f,point);
            float value=b2Dot(normal,point); low=fminf(low,value); high=fmaxf(high,value);
        }
        float margin=display_contact_margin(&a,&b,normal,true);
        if (broken) margin=-.03f; // Fixed world length must fail across sizes/directions.
        if (fabsf(-margin/(high-low)-.03f)>1e-6f) ++failures;
        if (fabsf(display_contact_margin(&b,&a,normal,true)-display_contact_margin(&a,&b,normal,true))>1e-6f) ++failures;
        if (fabsf(display_contact_margin(&a,&b,normal,false)-.001f)>1e-6f) ++failures;
    }
    b2Vec2 high={0,1},low={0,0},still={0,0},departing={0,1};
    if (!collision_support_eligible(.001f,1,high,low,still,still)) ++failures;
    if (collision_support_eligible(.001f,0,high,low,still,still) ||
        collision_support_eligible(.02f,1,high,low,still,still) ||
        collision_support_eligible(.001f,1,low,high,still,still) ||
        collision_support_eligible(.001f,1,high,low,departing,still)) ++failures;
    if (p->count==2)
    {
        // A sloped support must never steer the launch impulse sideways.
        b2Vec2 saved_positions[17],saved_speeds[2][17];
        for (int n=0;n<17;++n) saved_positions[n]=b2Body_GetPosition(display_id(p->bodies[1],n));
        for (int r=0;r<2;++r) for (int n=0;n<17;++n) saved_speeds[r][n]=b2Body_GetLinearVelocity(display_id(p->bodies[r],n));
        for (int side=-1;side<=1;side+=2)
        {
            b2Vec2 origin=b2Body_GetPosition(p->bodies[0]->core_body_id);
            b2Vec2 delta=b2Sub(b2Add(origin,(b2Vec2){side*.35f,1.65f}),saved_positions[16]);
            for (int n=0;n<17;++n) b2Body_SetTransform(display_id(p->bodies[1],n),b2Add(saved_positions[n],delta),b2Rot_identity);
            collision_launch(p,1,true,0);
            for (int r=0;r<2;++r) for (int n=0;n<17;++n)
            {
                b2BodyId id=display_id(p->bodies[r],n);
                if (fabsf(b2Body_GetLinearVelocity(id).x-saved_speeds[r][n].x)>1e-6f) ++failures;
                b2Body_SetLinearVelocity(id,saved_speeds[r][n]);
            }
        }
        for (int n=0;n<17;++n) b2Body_SetTransform(display_id(p->bodies[1],n),saved_positions[n],b2Rot_identity);
        b2Vec2 saved_velocity[2][17],before_total={0},after_total={0};
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[r],n); saved_velocity[r][n]=b2Body_GetLinearVelocity(id);
            before_total=b2MulAdd(before_total,b2Body_GetMass(id),saved_velocity[r][n]);
        }
        collision_launch(p,1,true,0);
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[r],n);
            after_total=b2MulAdd(after_total,b2Body_GetMass(id),b2Body_GetLinearVelocity(id));
            b2Body_SetLinearVelocity(id,saved_velocity[r][n]);
        }
        if (b2Distance(before_total,after_total)>1e-5f) ++failures;
    }
    b2Vec2 positions[17],before[17],after[17]; float masses[17];
    for (int n=0;n<17;++n)
    {
        float angle=2*B2_PI*(float)n/16;
        positions[n]=n==16 ? (b2Vec2){0,0} : (b2Vec2){cosf(angle),sinf(angle)};
        masses[n]=n==16 ? .12f : .015f;
        before[n]=(b2Vec2){2-3*positions[n].y+.3f*positions[n].x,-1+3*positions[n].x+.3f*positions[n].y};
    }
    for (int ground=0;ground<2;++ground) for (int active=0;active<2;++active)
    {
        memcpy(after,before,sizeof after);
        if (!broken) passive_spin(positions,after,masses,.1f,ground!=0,active!=0);
        b2Vec2 old_momentum={0},new_momentum={0}; double e0=0,e1=0;
        for (int n=0;n<17;++n)
        {
            float omega=ground && !active ? 3*expf(-12.0f) : 3;
            b2Vec2 expected={2-omega*positions[n].y+.3f*positions[n].x,-1+omega*positions[n].x+.3f*positions[n].y};
            if (b2Distance(after[n],expected)>2e-6f) ++failures;
            if ((!ground || active) && memcmp(&before[n],&after[n],sizeof(b2Vec2))) ++failures;
            old_momentum=b2MulAdd(old_momentum,masses[n],before[n]); new_momentum=b2MulAdd(new_momentum,masses[n],after[n]);
            e0+=masses[n]*b2Dot(before[n],before[n]); e1+=masses[n]*b2Dot(after[n],after[n]);
        }
        if (b2Distance(old_momentum,new_momentum)>1e-6f || e1-e0>1e-6) ++failures;
    }
    dango_self_soft_body* body=p->bodies[0];
    b2Vec2 saved[16];
    for (int n=0;n<16;++n) saved[n]=b2Body_GetPosition(body->node_body_ids[n]);
    for (int clipped=0;clipped<2;++clipped)
    {
        if (clipped) for (int n=0;n<16;++n)
        {
            b2Vec2 core=b2Body_GetPosition(body->core_body_id),local=b2Sub(saved[n],core);
            b2Body_SetTransform(body->node_body_ids[n],b2Add(core,(b2Vec2){2*local.x+.15f,1.7f*local.y}),b2Rot_identity);
        }
        display_mesh mesh; b2Vec2 g[17],normal={.6f,.8f};
        if (!display_build(body,p->config.node_radius,&mesh))
        {
            ++failures;
            for (int n=0;n<16;++n) b2Body_SetTransform(body->node_body_ids[n],saved[n],b2Rot_identity);
            continue;
        }
        display_gradient(&mesh,5,normal,g);
        for (int n=0;n<17;++n) for (int axis=0;axis<2;++axis)
        {
            b2BodyId id=display_id(body,n); b2Vec2 pos=b2Body_GetPosition(id),delta=axis ? (b2Vec2){0,.0005f} : (b2Vec2){.0005f,0};
            display_mesh plus,minus;
            b2Body_SetTransform(id,b2Add(pos,delta),b2Rot_identity); bool ok=display_build(body,p->config.node_radius,&plus);
            b2Body_SetTransform(id,b2Sub(pos,delta),b2Rot_identity); ok=display_build(body,p->config.node_radius,&minus) && ok;
            b2Body_SetTransform(id,pos,b2Rot_identity);
            if (!ok || fabsf(b2Dot(normal,b2Sub(plus.points[5],minus.points[5]))/.001f-(axis ? g[n].y : g[n].x))>.001f) ++failures;
        }
        for (int n=0;n<16;++n) b2Body_SetTransform(body->node_body_ids[n],saved[n],b2Rot_identity);
    }
    return failures;
}
static void collision_damp(collision_review* p,int r,float dt,const float* control)
{
    if (p->disable_damping_for_test) return;
    b2Vec2 positions[17],velocities[17]; float masses[17]; bool ground=false;
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[r],n);
        positions[n]=b2Body_GetPosition(id); velocities[n]=b2Body_GetLinearVelocity(id); masses[n]=b2Body_GetMass(id);
        if (n<16 && positions[n].y-p->config.node_radius<=p->config.ground_height+.001f) ground=true;
    }
    bool active=control[0]!=0 || control[1]!=0 || control[2]!=0 || control[3]!=0;
    passive_spin(positions,velocities,masses,dt,ground,active);
    if (ground && !active)
    {
        float mass=0,horizontal=0;
        for (int n=0;n<17;++n) { mass+=masses[n]; horizontal+=masses[n]*velocities[n].x; }
        // Finite dry friction on whole-body translation; no position anchor or vertical braking.
        float delta=b2ClampFloat(horizontal/mass,-35*dt,35*dt);
        for (int n=0;n<17;++n) velocities[n].x=(velocities[n].x-delta)*expf(-60*dt);
    }
    for (int n=0;n<17;++n) b2Body_SetLinearVelocity(display_id(p->bodies[r],n),velocities[n]);
}

DANGO_API void dango_collision_disable_for_test(void* handle)
{
    if (handle) ((collision_review*)handle)->disable_contact_for_test=1;
}

static bool collision_solve_pairs(collision_review* p,const bool* static_x,int* touched)
{
    bool relative=!(p->legacy_interaction_flags&64);
    for (int sweep=0;sweep<8;++sweep)
    {
        for (int a=0;a<p->capacity;++a) for (int b=a+1;b<p->capacity;++b)
        {
            if (!p->active[a] || !p->active[b]) continue;
            dango_self_soft_body* pair[2]={p->bodies[a],p->bodies[b]};
            bool fixed_x[2]={static_x[a],static_x[b]};
            if (!display_contact_solve(pair,&p->config,fixed_x,relative,touched)) return false;
        }
        if (p->count<=2) return true;
        bool valid=true;
        for (int a=0;a<p->capacity;++a) for (int b=a+1;b<p->capacity;++b)
        {
            if (!p->active[a] || !p->active[b]) continue;
            display_mesh m[2]; b2Vec2 normal; int ia,ib;
            if (!display_build(p->bodies[a],p->config.node_radius,&m[0]) || !display_build(p->bodies[b],p->config.node_radius,&m[1])) return false;
            float gap=display_separation(&m[0],&m[1],&normal,&ia,&ib,.001f);
            if (!isfinite(gap)) return false;
            if (gap<display_contact_margin(&m[0],&m[1],normal,relative)-1e-5f) valid=false;
        }
        if (valid) return true;
    }
    return false;
}

static int collision_local_floor_contacts(collision_review* p,dango_self_soft_body* body,int* grounded)
{
    int count=0;
    bool near_floor=false;
    for (int n=0;n<body->node_count;++n)
    {
        b2Vec2 position=b2Body_GetPosition(body->node_body_ids[n]);
        if (position.x>=p->horizontal_left && position.x<=p->horizontal_right &&
            position.y-p->config.node_radius<=p->config.ground_height+.035f) near_floor=true;
        b2ContactData contacts[4];
        int capacity=b2Body_GetContactCapacity(body->node_body_ids[n]);
        if (capacity<=0) continue;
        int touching=b2Body_GetContactData(body->node_body_ids[n],contacts,capacity<4 ? capacity : 4);
        for (int c=0;c<touching;++c)
        {
            b2ContactData* contact=&contacts[c];
            // The normal is A->B. Only the configured floor supporting a node
            // from below counts; walls and lateral floor edges cannot grant jumps.
            float upward=B2_ID_EQUALS(contact->shapeIdA,body->ground_shape_id) ? contact->manifold.normal.y :
                B2_ID_EQUALS(contact->shapeIdB,body->ground_shape_id) ? -contact->manifold.normal.y : 0;
            if (contact->manifold.pointCount>0 && upward>=.7f) ++count;
        }
    }
    body->grounded=count>0 || near_floor;
    *grounded=body->grounded ? 1 : 0;
    return count;
}

static int collision_read(collision_review* p, float* output)
{
    for (int r=0;r<p->capacity;++r)
    {
        if (!p->active[r]) continue;
        float* row=output+42*r; int points, contacts, grounded, selected;
        int error=dango_self_read_frame(p->bodies[r],&p->config,row,&points,&contacts,
            row+34,row+35,row+36,row+32,row+33,row+37,&grounded,&selected);
        if (error) return error;
        if (p->horizontal_bounds_enabled) contacts=collision_local_floor_contacts(p,p->bodies[r],&grounded);
        row[38]=(float)contacts; row[39]=(float)grounded; row[40]=(float)selected; row[41]=(float)points;
    }
    return 0;
}

DANGO_API int dango_collision_create(const dango_self_physics_config* config, int count, int layout, void** handle)
{
    if (!dango_self_config_valid(config) || !handle || count<1 || count>2 || layout<0 || layout>4) return -400;
    *handle=NULL;
    collision_review* p=calloc(1,sizeof *p); if (!p) return -401;
    p->config=*config; p->count=count; p->capacity=count; p->next_entity_id=1;
    p->preview_jump_speed_scale=1.0f;
    void* first=NULL;
    int error=dango_box2d_self_soft_body_create(config,&first);
    if (error) { free(p); return error; }
    p->bodies[0]=first;
    if (count==2)
    {
        p->bodies[1]=&p->additional[0];
        p->additional[0].world_id=p->bodies[0]->world_id;
        p->additional[0].ground_body_id=p->bodies[0]->ground_body_id;
        p->additional[0].ground_shape_id=p->bodies[0]->ground_shape_id;
        dango_self_create_rig(&p->additional[0],config);
    }
    for (int r=0;r<count;++r)
    {
        p->active[r]=1; p->entity_ids[r]=p->next_entity_id++;
        dango_self_initialize_at(p->bodies[r],config,r==0 ? 0 : layout==1 ? 0 : layout==2 ? 4.0f : layout==3 ? -.35f : layout==4 ? .35f : 2.8f,
            r==1 && (layout==1 || layout>=3) ? 1.65f : 0);
        p->anchors[2*r]=r==0 ? 0 : layout==1 ? 0 : layout==2 ? 4.0f : layout==3 ? -.35f : layout==4 ? .35f : 2.8f;
        p->anchors[2*r+1]=r==1 && (layout==1 || layout>=3) ? 1.65f : 0;
    }
    *handle=p;
    // Initial falling shape is real state, not a settled-pose replacement.
    p->error=collision_read(p,p->snapshot);
    return 0;
}

DANGO_API int dango_collision_step(void* handle, float dt, const float* controls, float* output, int capacity, int* contact_steps)
{
    collision_review* p=handle;
    if (!p || !controls || !output || capacity<p->capacity*42 || !contact_steps || !isfinite(dt) || dt<=0 || dt>.1f) return -400;
    for (int r=0;r<p->capacity;++r)
    {
        if (!p->active[r]) continue;
        const float* c=controls+6*r;
        for (int i=0;i<6;++i) if (!isfinite(c[i])) return -400;
        if (fabsf(c[0])>1 || c[1]<0 || c[1]>1 || (c[2]!=0 && c[2]!=1) || (c[3]!=0 && c[3]!=1)) return -400;
    }
    if (!p->error)
    {
        int small_steps=(int)ceilf(dt*(p->disable_damping_for_test ? 240.0f : 960.0f));
        if (small_steps<p->config.substep_count) small_steps=p->config.substep_count;
        float small_dt=dt/(float)small_steps;
        for (int sub=0;sub<small_steps && !p->error;++sub)
        {
            int supported[DANGO_PHYSICS_MAX_BODIES];
            for (int r=0;r<p->capacity;++r) supported[r]=-1;
            for (int r=0;r<p->capacity;++r) if (p->active[r]) supported[r]=collision_support_slot(p,r);
            for (int r=0;r<p->capacity;++r)
            {
                if (!p->active[r]) continue;
                const float* c=controls+6*r;
                dango_self_soft_body* body=p->bodies[r];
                bool idle=c[0]==0 && c[1]==0 && c[2]==0 && c[3]==0;
                float friction=!p->disable_damping_for_test && idle ? 16.0f : p->config.friction;
                for (int n=0;n<16;++n) b2Shape_SetFriction(body->node_shape_ids[n],friction);
                dango_self_apply_step_forces(body,&p->config,small_dt,sub,c[0],c[1],
                    (p->legacy_interaction_flags&1) ? (int)c[2] : 0,(int)c[3],c[4],c[5]);
                // Borrow only the accepted ground spreading force; do not change
                // actual grounding, air control, friction, or the support launch height.
                if (!(p->legacy_interaction_flags&128) && supported[r]>=0 && !body->grounded && c[2]==0 && c[3]==0)
                {
                    body->grounded=true;
                    dango_self_apply_ground_deformation_force(body,&p->config);
                    body->grounded=false;
                }
                if (!(p->legacy_interaction_flags&1) && sub==0 && c[2]!=0 && (body->grounded || supported[r]>=0) && body->seconds_since_jump>=p->config.jump_cooldown_seconds)
                {
                    collision_launch(p,r,!body->grounded && supported[r]>=0,supported[r]);
                }
            }
            b2World_Step(p->bodies[0]->world_id,small_dt,1);
            for (int r=0;r<p->capacity;++r)
                if (p->active[r])
                if (!dango_self_solve_constraints(p->bodies[r],&p->config)) p->error=-91;
            if (p->error) break;
            if (p->count>=2 && !p->disable_contact_for_test)
            {
                int touched=0;
                bool static_x[DANGO_PHYSICS_MAX_BODIES]={false};
                if (!p->disable_damping_for_test) for (int r=0;r<p->capacity;++r)
                {
                    if (!p->active[r]) continue;
                    const float* c=controls+6*r;
                    bool ground=false; float mass=0,horizontal=0;
                    for (int n=0;n<17;++n)
                    {
                        b2BodyId id=display_id(p->bodies[r],n); float m=b2Body_GetMass(id);
                        mass+=m; horizontal+=m*b2Body_GetLinearVelocity(id).x;
                        if (n<16 && b2Body_GetPosition(id).y-p->config.node_radius<=p->config.ground_height+.001f) ground=true;
                    }
                    static_x[r]=ground && c[0]==0 && c[1]==0 && c[2]==0 && c[3]==0 && fabsf(horizontal/mass)<=35*small_dt;
                }
                if (!collision_solve_pairs(p,static_x,&touched)) p->error=-402;
                p->contact_steps+=touched;
            }
            for (int r=0;r<p->capacity;++r) if (p->active[r]) collision_damp(p,r,small_dt,controls+6*r);
            for (int r=0;r<p->capacity;++r)
                if (p->active[r])
                if (!dango_self_limit_speed(p->bodies[r],p->config.maximum_speed)) p->error=-87;
            // Display convex-hull separation is checked by the contact solve; raw rig overlap is not visible penetration.
        }
        float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0};
        if (!p->error) p->error=collision_read(p,candidate);
        if (!p->error) memcpy(p->snapshot,candidate,sizeof candidate);
    }
    memcpy(output,p->snapshot,(size_t)p->capacity*42*sizeof(float)); *contact_steps=p->contact_steps;
    return p->error;
}

DANGO_API int dango_collision_destroy(void* handle)
{
    collision_review* p=handle; if (!p) return -400;
    int bytes=0; int error=dango_box2d_self_soft_body_destroy(p->bodies[0],&bytes);
    free(p); return error;
}

static bool collision_rig_valid(const dango_self_soft_body* body)
{
    if (!b2Body_IsValid(body->core_body_id) || !b2Shape_IsValid(body->core_shape_id)) return false;
    for (int n=0;n<body->node_count;++n)
        if (!b2Body_IsValid(body->node_body_ids[n]) || !b2Shape_IsValid(body->node_shape_ids[n])) return false;
    return true;
}

DANGO_API int dango_physics_get_abi_version(void)
{
    return DANGO_PHYSICS_MODULE_ABI_VERSION;
}

DANGO_API int dango_physics_world_create(const dango_self_physics_config* config,int capacity,void** handle)
{
    if (!handle || !dango_self_config_valid(config) || capacity<1 || capacity>DANGO_PHYSICS_MAX_BODIES) return -410;
    *handle=NULL;
    collision_review* p=calloc(1,sizeof *p); if (!p) return -401;
    p->config=*config; p->capacity=capacity; p->next_entity_id=1;
    p->preview_jump_speed_scale=1.0f;
    int error=dango_self_world_create(config,&p->bodies[0]);
    if (error) { free(p); return error; }
    *handle=p;
    return 0;
}

DANGO_API int dango_physics_world_add(void* handle,float anchor_x,float anchor_y,int* entity_id)
{
    collision_review* p=handle;
    if (!p || !entity_id || !isfinite(anchor_x) || !isfinite(anchor_y)) return -410;
    if (p->preview_jump_speed_scale!=1.0f && p->count!=0) return -410;
    if (p->count>=p->capacity) return -411;
    int slot=-1;
    for (int i=0;i<p->capacity;++i) if (!p->active[i]) { slot=i; break; }
    if (slot<0) return -411;
    if (slot>0)
    {
        p->bodies[slot]=&p->additional[slot-1];
        p->bodies[slot]->world_id=p->bodies[0]->world_id;
        p->bodies[slot]->ground_body_id=p->bodies[0]->ground_body_id;
        p->bodies[slot]->ground_shape_id=p->bodies[0]->ground_shape_id;
    }
    dango_self_create_rig(p->bodies[slot],&p->config);
    if (!collision_rig_valid(p->bodies[slot]))
    {
        dango_self_destroy_rig(p->bodies[slot]);
        return -401;
    }
    dango_self_initialize_at(p->bodies[slot],&p->config,anchor_x,anchor_y);
    p->active[slot]=1; p->entity_ids[slot]=p->next_entity_id++;
    p->anchors[2*slot]=anchor_x; p->anchors[2*slot+1]=anchor_y; ++p->count;
    memset(p->snapshot+42*slot,0,42*sizeof(float));
    int error=collision_read(p,p->snapshot);
    if (error)
    {
        dango_self_destroy_rig(p->bodies[slot]); p->active[slot]=0; p->entity_ids[slot]=0; --p->count;
        return error;
    }
    *entity_id=p->entity_ids[slot];
    return 0;
}

DANGO_API int dango_physics_world_remove(void* handle,int entity_id)
{
    collision_review* p=handle;
    if (!p || entity_id<=0) return -410;
    int slot=-1;
    for (int i=0;i<p->capacity;++i) if (p->active[i] && p->entity_ids[i]==entity_id) { slot=i; break; }
    if (slot<0) return -412;
    dango_self_destroy_rig(p->bodies[slot]);
    p->active[slot]=0; p->entity_ids[slot]=0; --p->count;
    memset(p->snapshot+42*slot,0,42*sizeof(float));
    return 0;
}

DANGO_API int dango_physics_world_step(void* handle,float time_step,const int* entity_ids,const float* controls,
    int control_count,int* state_ids,float* states,int state_capacity,int* state_count,int* contact_steps,int* latched_error)
{
    collision_review* p=handle;
    if (!p || control_count!=p->count || (control_count>0 && (!entity_ids || !controls)) || !state_ids || !states ||
        state_capacity<p->count*DANGO_PHYSICS_STATE_FLOATS || !state_count || !contact_steps || !latched_error) return -410;
    if (p->count==0)
    {
        if (!isfinite(time_step) || time_step<=0 || time_step>.1f) return -410;
        *state_count=0; *contact_steps=p->contact_steps; *latched_error=p->error;
        return 0;
    }
    float slot_controls[DANGO_PHYSICS_MAX_BODIES*DANGO_PHYSICS_CONTROL_FLOATS]={0};
    int seen[DANGO_PHYSICS_MAX_BODIES]={0};
    for (int index=0;index<control_count;++index)
    {
        int slot=-1;
        for (int i=0;i<p->capacity;++i) if (p->active[i] && p->entity_ids[i]==entity_ids[index]) { slot=i; break; }
        if (slot<0 || seen[slot]) return -412;
        seen[slot]=1; memcpy(slot_controls+slot*6,controls+index*6,6*sizeof(float));
    }
    float slot_states[DANGO_PHYSICS_MAX_BODIES*DANGO_PHYSICS_STATE_FLOATS]={0};
    int error=dango_collision_step(p,time_step,slot_controls,slot_states,DANGO_PHYSICS_MAX_BODIES*42,contact_steps);
    if (error==-400) return -410;
    int output=0;
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot])
    {
        state_ids[output]=p->entity_ids[slot];
        memcpy(states+output*42,slot_states+slot*42,42*sizeof(float));
        ++output;
    }
    *state_count=output; *latched_error=p->error;
    return 0;
}

DANGO_API int dango_physics_world_reset(void* handle)
{
    collision_review* p=handle;
    if (!p) return -410;
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot]) dango_self_destroy_rig(p->bodies[slot]);
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot])
    {
        dango_self_create_rig(p->bodies[slot],&p->config);
        if (!collision_rig_valid(p->bodies[slot])) return -401;
        dango_self_initialize_at(p->bodies[slot],&p->config,p->anchors[2*slot],p->anchors[2*slot+1]);
    }
    p->error=0; p->contact_steps=0; memset(p->snapshot,0,sizeof p->snapshot);
    return collision_read(p,p->snapshot);
}

// Additive ABI-1 capability. This changes only future launch impulses, never
// the existing rig, current velocities, cooldown, configuration or step count.
// Restoring the default remains legal after a latched fault for host cleanup.
DANGO_API int dango_physics_world_set_preview_jump_speed_scale(void* handle,float scale)
{
    collision_review* p=handle;
    if (!p || !isfinite(scale) || scale<.1f || scale>1.0f ||
        (scale!=1.0f && (p->error || p->count!=1))) return -410;
    p->preview_jump_speed_scale=scale;
    return 0;
}

// Opt-in local scene geometry. Existing worlds retain their original floor and
// exact stepping path unless these additive ABI-1 exports are explicitly used.
DANGO_API int dango_physics_world_set_horizontal_bounds(void* handle,float left,float right)
{
    collision_review* p=handle;
    if (!p || p->error || !isfinite(left) || !isfinite(right) ||
        fabsf(left)>1024 || fabsf(right)>1024 ||
        !isfinite(right-left) || right-left<2*(p->config.radial_rest_length+p->config.node_radius)) return -410;
    if (p->horizontal_bounds_enabled && left==p->horizontal_left && right==p->horizontal_right) return 0;
    // Reject a shrinking floor that would strand an existing rig; no teleport or
    // partial geometry mutation is used to make invalid bounds appear successful.
    // Pinned Box2D 3.1.1 constants.h defines LINEAR_SLOP as .005 meters. A legal
    // resting contact may overlap by this solver tolerance; .001 incorrectly
    // rejected the unchanged wall after recentering a real contact pose.
    float containment_slop=.005f*b2GetLengthUnitsPerMeter();
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        for (int n=0;n<16;++n)
        {
            float x=b2Body_GetPosition(p->bodies[r]->node_body_ids[n]).x;
            if (x-p->config.node_radius<left-containment_slop || x+p->config.node_radius>right+containment_slop) return -410;
        }
    dango_self_soft_body* first=p->bodies[0];
    b2ShapeDef wall_def=b2DefaultShapeDef();
    wall_def.material.friction=p->config.friction;
    wall_def.material.restitution=p->config.restitution;
    wall_def.filter.categoryBits=0x00000001;
    wall_def.filter.maskBits=0x00000002;
    b2Polygon walls[2]={
        b2MakeOffsetBox(.08f,32.0f,(b2Vec2){left-.08f,32.08f},b2Rot_identity),
        b2MakeOffsetBox(.08f,32.0f,(b2Vec2){right+.08f,32.08f},b2Rot_identity)};
    bool created[2]={false,false};
    for (int i=0;i<2;++i)
    {
        if (b2Shape_IsValid(p->horizontal_walls[i])) b2Shape_SetPolygon(p->horizontal_walls[i],&walls[i]);
        else
        {
            p->horizontal_walls[i]=b2CreatePolygonShape(first->ground_body_id,&wall_def,&walls[i]);
            created[i]=b2Shape_IsValid(p->horizontal_walls[i]);
        }
        if (!b2Shape_IsValid(p->horizontal_walls[i]))
        {
            for (int j=0;j<2;++j) if (created[j])
            {
                b2DestroyShape(p->horizontal_walls[j],false);
                p->horizontal_walls[j]=b2_nullShapeId;
            }
            // A partial allocation is not usable geometry. Preserve the last
            // safe snapshot and require explicit recovery rather than stepping.
            p->error=-401;
            return p->error;
        }
    }
    b2Body_SetTransform(first->ground_body_id,(b2Vec2){0,p->config.ground_height-.08f},b2Rot_identity);
    b2Polygon floor=b2MakeOffsetBox((right-left)/2,.08f,(b2Vec2){left+(right-left)/2,0},b2Rot_identity);
    b2Shape_SetPolygon(first->ground_shape_id,&floor);
    p->horizontal_left=left; p->horizontal_right=right; p->horizontal_bounds_enabled=true;
    return 0;
}

DANGO_API int dango_physics_world_clear_horizontal_bounds(void* handle)
{
    collision_review* p=handle;
    if (!p || p->error) return -410;
    dango_self_soft_body* first=p->bodies[0];
    if (!p->horizontal_bounds_enabled && b2Body_GetPosition(first->ground_body_id).x==0 &&
        !b2Shape_IsValid(p->horizontal_walls[0]) && !b2Shape_IsValid(p->horizontal_walls[1])) return 0;
    for (int i=0;i<2;++i)
        if (b2Shape_IsValid(p->horizontal_walls[i]))
        {
            b2DestroyShape(p->horizontal_walls[i],false);
            p->horizontal_walls[i]=b2_nullShapeId;
        }
    b2Body_SetTransform(first->ground_body_id,(b2Vec2){0,p->config.ground_height-.08f},b2Rot_identity);
    b2Polygon floor=b2MakeBox(8.0f,.08f);
    b2Shape_SetPolygon(first->ground_shape_id,&floor);
    p->horizontal_bounds_enabled=false;
    return 0;
}

DANGO_API int dango_physics_world_translate_horizontal(void* handle,float translation)
{
    collision_review* p=handle;
    if (!p || p->error || !isfinite(translation) || fabsf(translation)>1024) return -410;
    if (translation==0) return 0;
    if (fabsf(b2Body_GetPosition(p->bodies[0]->ground_body_id).x+translation)>1024 ||
        (p->horizontal_bounds_enabled && (fabsf(p->horizontal_left+translation)>1024 ||
            fabsf(p->horizontal_right+translation)>1024))) return -410;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        for (int n=0;n<17;++n)
        {
            b2Vec2 position=b2Body_GetPosition(display_id(p->bodies[r],n));
            if (!isfinite(position.x+translation) || fabsf(position.x+translation)>1024) return -410;
        }
    // All nodes, the core and floor move together. Velocities, entity IDs,
    // relative shape, joints, cooldowns and the reset anchors are not recreated.
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[r],n);
            b2Vec2 position=b2Body_GetPosition(id); position.x+=translation;
            b2Body_SetTransform(id,position,b2Body_GetRotation(id));
        }
    b2BodyId ground=p->bodies[0]->ground_body_id;
    b2Vec2 position=b2Body_GetPosition(ground); position.x+=translation;
    b2Body_SetTransform(ground,position,b2Body_GetRotation(ground));
    if (p->horizontal_bounds_enabled)
    {
        p->horizontal_left+=translation;
        p->horizontal_right+=translation;
    }
    return collision_read(p,p->snapshot);
}

DANGO_API int dango_physics_world_destroy(void* handle)
{
    collision_review* p=handle;
    if (!p || !p->bodies[0]) return -410;
    if (b2World_IsValid(p->bodies[0]->world_id)) b2DestroyWorld(p->bodies[0]->world_id);
    free(p->bodies[0]); free(p);
    return 0;
}

// Collision geometry follows the unchanged WPF quadratic B-spline, not the larger raw rig.
#define DISPLAY_SAMPLES 128
#define DISPLAY_SAMPLES_PER_NODE (DISPLAY_SAMPLES/16)
typedef struct display_mesh
{
    b2Vec2 local[16], expanded[16], points[DISPLAY_SAMPLES];
    float scale, maximum, radius;
    int max_node, max_axis;
} display_mesh;

static bool display_build(dango_self_soft_body* body, float radius, display_mesh* mesh)
{
    b2Vec2 core=b2Body_GetPosition(body->core_body_id);
    memset(mesh,0,sizeof *mesh); mesh->radius=radius;
    for (int n=0;n<16;++n)
    {
        b2Vec2 local=b2Sub(b2Body_GetPosition(body->node_body_ids[n]),core);
        float length=b2Length(local); if (!isfinite(length) || length<1e-6f) return false;
        mesh->local[n]=local; mesh->expanded[n]=b2MulSV(1+radius/length,local);
        for (int axis=0;axis<2;++axis)
        {
            float value=fabsf(axis ? mesh->expanded[n].y : mesh->expanded[n].x);
            if (value>mesh->maximum) { mesh->maximum=value; mesh->max_node=n; mesh->max_axis=axis; }
        }
    }
    mesh->scale=fminf(1,.98f/mesh->maximum);
    for (int s=0;s<DISPLAY_SAMPLES;++s)
    {
        int n=s/DISPLAY_SAMPLES_PER_NODE; float t=(float)(s%DISPLAY_SAMPLES_PER_NODE)/DISPLAY_SAMPLES_PER_NODE;
        b2Vec2 q=b2MulSV(.5f*(1-t)*(1-t),mesh->expanded[(n+15)%16]);
        q=b2MulAdd(q,.5f+t-t*t,mesh->expanded[n]);
        q=b2MulAdd(q,.5f*t*t,mesh->expanded[(n+1)%16]);
        mesh->points[s]=b2Add(core,(b2Vec2){q.x*mesh->scale*.9f,q.y*mesh->scale*(58.0f/60)});
    }
    // SAT requires convex ordered input; reject violations instead of using an invisible convex hull.
    for (int s=0;s<DISPLAY_SAMPLES;++s)
    {
        b2Vec2 e=b2Sub(mesh->points[(s+1)%DISPLAY_SAMPLES],mesh->points[s]);
        b2Vec2 f=b2Sub(mesh->points[(s+2)%DISPLAY_SAMPLES],mesh->points[(s+1)%DISPLAY_SAMPLES]);
        if (!b2IsValidVec2(mesh->points[s]) || b2Dot(e,e)<1e-12f || b2Cross(e,f)<-1e-8f) return false;
    }
    return true;
}

static b2Vec2 display_expand_gradient(const display_mesh* mesh, int n, b2Vec2 g)
{
    b2Vec2 p=mesh->local[n]; float l=b2Length(p);
    return b2Sub(b2MulSV(1+mesh->radius/l,g),b2MulSV(mesh->radius*b2Dot(p,g)/(l*l*l),p));
}

static void display_gradient(const display_mesh* mesh, int sample, b2Vec2 normal, b2Vec2* gradient)
{
    memset(gradient,0,17*sizeof *gradient);
    int n=sample/DISPLAY_SAMPLES_PER_NODE; float t=(float)(sample%DISPLAY_SAMPLES_PER_NODE)/DISPLAY_SAMPLES_PER_NODE;
    int nodes[3]={(n+15)%16,n,(n+1)%16};
    float weights[3]={.5f*(1-t)*(1-t),.5f+t-t*t,.5f*t*t};
    b2Vec2 mapped={.9f*normal.x,(58.0f/60)*normal.y}, sum={0};
    for (int j=0;j<3;++j)
    {
        int k=nodes[j]; sum=b2MulAdd(sum,weights[j],mesh->expanded[k]);
        gradient[k]=b2Add(gradient[k],display_expand_gradient(mesh,k,b2MulSV(mesh->scale*weights[j],mapped)));
    }
    if (mesh->scale<1)
    {
        int k=mesh->max_node;
        float component=mesh->max_axis ? mesh->expanded[k].y : mesh->expanded[k].x;
        float derivative=-mesh->scale/mesh->maximum*b2Dot(mapped,sum)*(component<0 ? -1 : 1);
        b2Vec2 g=mesh->max_axis ? (b2Vec2){0,derivative} : (b2Vec2){derivative,0};
        gradient[k]=b2Add(gradient[k],display_expand_gradient(mesh,k,g));
    }
    gradient[16]=normal;
    for (int k=0;k<16;++k) gradient[16]=b2Sub(gradient[16],gradient[k]);
}

static float display_separation(const display_mesh* a, const display_mesh* b, b2Vec2* normal, int* ia, int* ib, float margin)
{
    float best=-INFINITY;
    for (int r=0;r<2;++r) for (int s=0;s<DISPLAY_SAMPLES;++s)
    {
        const display_mesh* m=r ? b : a;
        b2Vec2 e=b2Sub(m->points[(s+1)%DISPLAY_SAMPLES],m->points[s]);
        float length=b2Length(e); if (length<1e-8f) continue;
        b2Vec2 n={e.y/length,-e.x/length};
        float amin=INFINITY,amax=-INFINITY,bmin=INFINITY,bmax=-INFINITY;
        int amin_i=0,amax_i=0,bmin_i=0,bmax_i=0;
        for (int k=0;k<DISPLAY_SAMPLES;++k)
        {
            float av=b2Dot(a->points[k],n),bv=b2Dot(b->points[k],n);
            if (av<amin) { amin=av; amin_i=k; } if (av>amax) { amax=av; amax_i=k; }
            if (bv<bmin) { bmin=bv; bmin_i=k; } if (bv>bmax) { bmax=bv; bmax_i=k; }
        }
        float gap=bmin-amax; int ai=amax_i,bi=bmin_i;
        if (amin-bmax>gap) { gap=amin-bmax; n=b2Neg(n); ai=amin_i; bi=bmax_i; }
        if (gap>best) { best=gap; *normal=n; *ia=ai; *ib=bi; }
        if (best>margin) return best;
    }
    return best;
}

static b2BodyId display_id(dango_self_soft_body* body,int n) { return n==16 ? body->core_body_id : body->node_body_ids[n]; }


static float display_projected_width(const display_mesh* mesh,b2Vec2 normal)
{
    float low=INFINITY,high=-INFINITY;
    for (int i=0;i<DISPLAY_SAMPLES;++i)
    {
        float value=b2Dot(mesh->points[i],normal);
        low=fminf(low,value); high=fmaxf(high,value);
    }
    return high-low;
}

static float display_contact_margin(const display_mesh* a,const display_mesh* b,b2Vec2 normal,bool relative_overlap)
{
    if (!relative_overlap) return .001f; // Explicit historical no-overlap test mode.
    return -.03f*fminf(display_projected_width(a,normal),display_projected_width(b,normal));
}

static bool display_contact_solve(dango_self_soft_body** bodies,const dango_self_physics_config* const* config,const bool* static_x,bool relative_overlap,int* touched)
{
    for (int iteration=0;iteration<96;++iteration)
    {
        display_mesh m[2]; b2Vec2 normal; int ia,ib;
        if (!display_build(bodies[0],config[0]->node_radius,&m[0]) || !display_build(bodies[1],config[1]->node_radius,&m[1])) return false;
        float gap=display_separation(&m[0],&m[1],&normal,&ia,&ib,.001f);
        if (!isfinite(gap)) return false;
        float margin=display_contact_margin(&m[0],&m[1],normal,relative_overlap);
        if (gap>=margin-1e-5f) return true;
        *touched=1;
        b2Vec2 g[2][17]; display_gradient(&m[0],ia,b2Neg(normal),g[0]); display_gradient(&m[1],ib,normal,g[1]);
        b2Vec2 position_g[2][17]; memcpy(position_g,g,sizeof g);
        // Static ground friction resists rigid translation during positional contact correction,
        // while all relative node deformation stays mobile. No remembered world-space anchor.
        for (int r=0;r<2;++r) if (static_x[r])
        {
            float mass=0,sum=0;
            for (int n=0;n<17;++n) { mass+=b2Body_GetMass(display_id(bodies[r],n)); sum+=g[r][n].x; }
            for (int n=0;n<17;++n) position_g[r][n].x-=sum*b2Body_GetMass(display_id(bodies[r],n))/mass;
        }
        float position_k=0;
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
            position_k+=b2Dot(g[r][n],position_g[r][n])/b2Body_GetMass(display_id(bodies[r],n));
        float k=0,vn=0;
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(bodies[r],n);
            k+=b2Dot(g[r][n],g[r][n])/b2Body_GetMass(id);
            vn+=b2Dot(g[r][n],b2Body_GetLinearVelocity(id));
        }
        if (!isfinite(k) || k<=0) return false;
        if (!isfinite(position_k) || position_k<=0) return false;
        float correction=fminf(margin-gap,.01f)/position_k,impulse=fmaxf(0,-vn)/k;
        double before=0,after=0; b2Vec2 momentum_before={0},momentum_after={0};
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(bodies[r],n); float inv=1/b2Body_GetMass(id);
            b2Vec2 old=b2Body_GetLinearVelocity(id); before+=.5/inv*b2Dot(old,old);
            momentum_before=b2MulAdd(momentum_before,1/inv,old);
            b2Body_SetTransform(id,b2MulAdd(b2Body_GetPosition(id),inv*correction,position_g[r][n]),b2Rot_identity);
            b2Body_SetLinearVelocity(id,b2MulAdd(b2Body_GetLinearVelocity(id),inv*impulse,g[r][n]));
            b2Vec2 next=b2Body_GetLinearVelocity(id); after+=.5/inv*b2Dot(next,next);
            momentum_after=b2MulAdd(momentum_after,1/inv,next);
        }
        if (after-before>1e-6 || b2Distance(momentum_before,momentum_after)>1e-5f) return false;
        for (int r=0;r<2;++r) if (!dango_self_solve_constraints(bodies[r],config[r])) return false;
    }
    return false;
}

static void passive_spin(b2Vec2* positions,b2Vec2* velocities,const float* masses,float dt,bool grounded,bool active)
{
    if (!grounded || active) return;
    float mass=0,inertia=0,momentum=0; b2Vec2 center={0},translation={0};
    for (int n=0;n<17;++n) { mass+=masses[n]; center=b2MulAdd(center,masses[n],positions[n]); translation=b2MulAdd(translation,masses[n],velocities[n]); }
    center=b2MulSV(1/mass,center); translation=b2MulSV(1/mass,translation);
    for (int n=0;n<17;++n)
    {
        b2Vec2 r=b2Sub(positions[n],center);
        inertia+=masses[n]*b2Dot(r,r); momentum+=masses[n]*b2Cross(r,b2Sub(velocities[n],translation));
    }
    if (inertia<1e-8f) return;
    float delta=(expf(-120*dt)-1)*momentum/inertia;
    for (int n=0;n<17;++n) { b2Vec2 r=b2Sub(positions[n],center); velocities[n]=b2MulAdd(velocities[n],delta,(b2Vec2){-r.y,r.x}); }
}

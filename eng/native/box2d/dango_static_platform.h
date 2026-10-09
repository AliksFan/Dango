// One solid static rectangle. Contacts use the same display curve as inter-body
// contacts, never a second raw-node collider or a presentation-layer correction.
#ifndef DANGO_STATIC_PLATFORM_H
#define DANGO_STATIC_PLATFORM_H

#ifndef DANGO_STATIC_PLATFORM_TEST_VARIANT
#define DANGO_STATIC_PLATFORM_TEST_VARIANT 0
#endif

typedef struct dango_static_platform
{
    float x,y,width,height,friction;
} dango_static_platform;

typedef struct dango_platform_contact
{
    b2Vec2 normal;
    float gap;
    int sample;
} dango_platform_contact;

static bool platform_valid(const dango_static_platform* p)
{
    return isfinite(p->x) && isfinite(p->y) && isfinite(p->width) && isfinite(p->height) &&
        isfinite(p->friction) && p->width>=.1f && p->width<=16 && p->height>=.1f && p->height<=16 &&
        p->friction>=0 && p->friction<=1.5f &&
        fabsf(p->x-p->width*.5f)<=1024 && fabsf(p->x+p->width*.5f)<=1024 &&
        fabsf(p->y-p->height*.5f)<=1024 && fabsf(p->y+p->height*.5f)<=1024;
}

static void platform_axis(const display_mesh* mesh,const dango_static_platform* p,
    b2Vec2 axis,dango_platform_contact* contact)
{
    float low=INFINITY,high=-INFINITY;
    int low_sample=0,high_sample=0;
    for (int s=0;s<DISPLAY_SAMPLES;++s)
    {
        float projection=b2Dot(mesh->points[s],axis);
        if (projection<low) { low=projection; low_sample=s; }
        if (projection>high) { high=projection; high_sample=s; }
    }
    float center=p->x*axis.x+p->y*axis.y;
    float extent=.5f*(p->width*fabsf(axis.x)+p->height*fabsf(axis.y));
    float gap=low-(center+extent);
    if (gap>contact->gap) { contact->gap=gap; contact->normal=axis; contact->sample=low_sample; }
    gap=(center-extent)-high;
    if (gap>contact->gap) { contact->gap=gap; contact->normal=b2Neg(axis); contact->sample=high_sample; }
}

static dango_platform_contact platform_separation(const display_mesh* mesh,const dango_static_platform* p)
{
    dango_platform_contact contact={.gap=-INFINITY};
    platform_axis(mesh,p,(b2Vec2){1,0},&contact);
    platform_axis(mesh,p,(b2Vec2){0,1},&contact);
    if (contact.gap>.008f) return contact;
    // When the curve is fully within a face span, no rectangle corner can be
    // the closest feature. Avoid 128 redundant SAT axes in the resting case.
    float left=INFINITY,right=-INFINITY,bottom=INFINITY,top=-INFINITY;
    for (int s=0;s<DISPLAY_SAMPLES;++s)
    {
        left=fminf(left,mesh->points[s].x); right=fmaxf(right,mesh->points[s].x);
        bottom=fminf(bottom,mesh->points[s].y); top=fmaxf(top,mesh->points[s].y);
    }
    if ((left>=p->x-p->width*.5f && right<=p->x+p->width*.5f && fabsf(contact.normal.y)>.99f) ||
        (bottom>=p->y-p->height*.5f && top<=p->y+p->height*.5f && fabsf(contact.normal.x)>.99f)) return contact;
    for (int s=0;s<DISPLAY_SAMPLES;++s)
    {
        b2Vec2 edge=b2Sub(mesh->points[(s+1)%DISPLAY_SAMPLES],mesh->points[s]);
        float length=b2Length(edge);
        if (length<1e-8f) continue;
        platform_axis(mesh,p,(b2Vec2){edge.y/length,-edge.x/length},&contact);
    }
    return contact;
}

static bool platform_support_eligible(const dango_platform_contact* contact,float core_y,float velocity_y,
    const dango_static_platform* platform)
{
    bool upward=contact->normal.y>=.7f && core_y>platform->y+platform->height*.5f;
#if DANGO_STATIC_PLATFORM_TEST_VARIANT == 2
    upward=upward || fabsf(contact->normal.x)>=.7f; // Deliberately broken test build only.
#endif
    return contact->gap<=.008f && upward && velocity_y<.5f;
}

static bool platform_contact_solve(dango_self_soft_body* body,const dango_self_physics_config* config,
    const dango_static_platform* platform,int* touched)
{
#if DANGO_STATIC_PLATFORM_TEST_VARIANT == 1
    (void)body; (void)config; (void)platform; (void)touched;
    return true; // Deliberately absent collision in the negative test build.
#else
    for (int iteration=0;iteration<96;++iteration)
    {
        display_mesh mesh;
        if (!display_build(body,config->node_radius,&mesh)) return false;
        dango_platform_contact contact=platform_separation(&mesh,platform);
        if (!isfinite(contact.gap)) return false;
        if (contact.gap>.008f) return true;
        b2Vec2 gradient[17];
        display_gradient(&mesh,contact.sample,contact.normal,gradient);
        float effective_inverse_mass=0,closing_speed=0;
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(body,n);
            effective_inverse_mass+=b2Dot(gradient[n],gradient[n])/b2Body_GetMass(id);
            closing_speed+=b2Dot(gradient[n],b2Body_GetLinearVelocity(id));
        }
        if (!isfinite(effective_inverse_mass) || effective_inverse_mass<=0 || !isfinite(closing_speed)) return false;
        float correction=fminf(fmaxf(0,.0005f-contact.gap),.01f)/effective_inverse_mass;
        float impulse=fmaxf(0,-closing_speed)/effective_inverse_mass;
        if (correction==0 && impulse==0) return true;
        *touched=1;
        double before=0,after=0;
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(body,n); float mass=b2Body_GetMass(id);
            b2Vec2 old=b2Body_GetLinearVelocity(id); before+=.5*mass*b2Dot(old,old);
            if (correction>0)
                b2Body_SetTransform(id,b2MulAdd(b2Body_GetPosition(id),correction/mass,gradient[n]),b2Rot_identity);
            if (impulse>0) b2Body_SetLinearVelocity(id,b2MulAdd(old,impulse/mass,gradient[n]));
            b2Vec2 next=b2Body_GetLinearVelocity(id); after+=.5*mass*b2Dot(next,next);
        }
        if (after-before>1e-6) return false; // Fixed support can remove, never inject, kinetic energy.
        if (!dango_self_solve_constraints(body,config)) return false;
        if (contact.gap>=-1e-5f && correction<1e-6f) return true;
    }
    display_mesh final;
    if (!display_build(body,config->node_radius,&final)) return false;
    return platform_separation(&final,platform).gap>=-1e-5f;
#endif
}

#endif

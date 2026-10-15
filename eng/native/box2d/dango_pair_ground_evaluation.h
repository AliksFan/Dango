// Isolated two/three-rig adapter. Never included in the production DLL.
typedef struct pair_ground_context
{
    dango_self_soft_body* body;
    b2Vec2 previous[17];
    float normals[16], tangents[16];
} pair_ground_context;
static pair_ground_context pair_ground[3];
static int ground_group_count;

static b2BodyId pair_ground_id(dango_self_soft_body* body, int i)
{
    return i==16 ? body->core_body_id : body->node_body_ids[i];
}

static void group_ground_step(dango_self_soft_body** bodies, int count, float dt)
{
    ground_group_count=count;
    if (ground_candidate)
        for (int k=0;k<count;++k)
        {
            pair_ground[k].body=bodies[k];
            for (int i=0;i<17;++i)
                pair_ground[k].previous[i]=b2Body_GetPosition(pair_ground_id(bodies[k],i));
        }
    // One shared world step, irrespective of the number of awake rigs.
    b2World_Step(bodies[0]->world_id,dt,1);
    ground_dt=dt;
    if (!ground_candidate) return;
    for (int k=0;k<count;++k) for (int i=0;i<16;++i)
    {
        pair_ground[k].normals[i]=pair_ground[k].tangents[i]=0;
        b2ContactData contacts[8];
        int contact_count=b2Body_GetContactData(bodies[k]->node_body_ids[i],contacts,8);
        for (int j=0;j<contact_count;++j)
        {
            bool floor_a=B2_ID_EQUALS(contacts[j].shapeIdA,bodies[k]->ground_shape_id);
            bool floor_b=B2_ID_EQUALS(contacts[j].shapeIdB,bodies[k]->ground_shape_id);
            if (!floor_a && !floor_b) continue;
            for (int p=0;p<contacts[j].manifold.pointCount;++p)
            {
                pair_ground[k].normals[i]+=contacts[j].manifold.points[p].normalImpulse;
                pair_ground[k].tangents[i]+=(floor_a ? 1.0f : -1.0f)*
                    contacts[j].manifold.normal.y*contacts[j].manifold.points[p].tangentImpulse;
            }
        }
    }
}

static void pair_ground_step(dango_self_soft_body** bodies, float dt)
{
    group_ground_step(bodies,2,dt);
}

static bool pair_ground_solve(dango_self_soft_body* body, const dango_self_physics_config* config)
{
    if (ground_candidate)
    {
        int k=-1;
        for (int i=0;i<ground_group_count;++i) if (pair_ground[i].body==body) k=i;
        if (k<0) return false;
        memcpy(ground_previous,pair_ground[k].previous,sizeof ground_previous);
        memcpy(ground_normal_impulses,pair_ground[k].normals,sizeof ground_normal_impulses);
        memcpy(ground_tangent_impulses,pair_ground[k].tangents,sizeof ground_tangent_impulses);
        ground_mu=config->friction;
    }
    return dango_self_solve_constraints(body,config);
}

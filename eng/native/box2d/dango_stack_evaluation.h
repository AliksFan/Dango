// Native diagnostic only: no artificial centering, pinning, or stack sleep.
static int contact_friction_tests(void)
{
    for (int side=-1;side<=1;side+=2) for (int endpoint=0;endpoint<=4;++endpoint)
        for (int mode=0;mode<4;++mode)
        {
            float t=(float)endpoint/4,weights[3]={1,-(1-t),-t};
            float inv[3]={1/.015f,1/.02f,1/.03f};
            b2Vec2 tangent={.6f,.8f};
            b2Vec2 v[3]={b2MulSV(side*(mode==3 ? .001f : 3),tangent),{0,0},{0,0}};
            b2Vec2 before[3]; memcpy(before,v,sizeof v);
            float mu=mode==1 ? 0 : .3f,normal=mode==2 ? 0 : .03f;
            float j=contact_friction_apply(v,inv,weights,tangent,normal,mu);
            CHECK(fabsf(j)<=mu*normal+1e-8f);
            b2Vec2 p0={0},p1={0}; float e0=0,e1=0,speed0=0,speed1=0;
            for (int i=0;i<3;++i)
            {
                p0=b2MulAdd(p0,1/inv[i],before[i]); p1=b2MulAdd(p1,1/inv[i],v[i]);
                e0+=.5f*b2LengthSquared(before[i])/inv[i]; e1+=.5f*b2LengthSquared(v[i])/inv[i];
                speed0+=weights[i]*b2Dot(before[i],tangent); speed1+=weights[i]*b2Dot(v[i],tangent);
                if (mode==1 || mode==2) CHECK(b2Distance(v[i],before[i])==0);
            }
            CHECK(b2Distance(p0,p1)<1e-7f && e1<=e0+1e-7f);
            if (mode==0 || mode==3)
            {
                CHECK(fabsf(speed1)<fabsf(speed0));
                CHECK(speed0*speed1>=-1e-6f);
                if (mode==0) CHECK(fabsf(fabsf(j)-mu*normal)<1e-7f);
                if (mode==3) CHECK(fabsf(speed1)<1e-7f);
            }
        }
    printf("PASS contact friction signs/endpoints/budget/zero-support/zero-mu/momentum/energy\n");
    return 0;
}
static int stack_order_mode;
static int stack_substep_index;
static bool stack_trace;
static bool stack_residual_trace;
static bool stack_continue_diagnostic;
static int stack_iterations_used;
static double stack_contact_dx[3],stack_self_dx[3],stack_world_dx[3];
static double stack_contact_px[3],stack_self_px[3],stack_world_px[3];
static double stack_mass_x(dango_self_soft_body* body,bool momentum)
{
    double value=0;
    for (int i=0;i<17;++i)
        value+=(double)b2Body_GetMass(rig_id(body,i))*(momentum ?
            b2Body_GetLinearVelocity(rig_id(body,i)).x : b2Body_GetPosition(rig_id(body,i)).x);
    return value;
}
static bool stack_geometry(dango_self_soft_body** bodies,int count,
    const dango_self_physics_config* config,float skin)
{
    for (int k=0;k<count;++k) if (!legal(bodies[k],config)) return false;
    for (int a=0;a<count;++a) for (int b=a+1;b<count;++b)
        for (int pass=0;pass<2;++pass) for (int i=0;i<16;++i)
        {
            dango_self_soft_body* point=bodies[pass ? b : a];
            dango_self_soft_body* edge=bodies[pass ? a : b];
            if (evaluate_node_edge(point,i,edge,skin,false)>1e-5f) return false;
            b2Vec2 p=b2Body_GetPosition(rig_id(point,i));
            if (b2Distance(p,b2Body_GetPosition(edge->core_body_id))<.17f) return false;
            b2Vec2 q=b2Body_GetPosition(rig_id(point,(i+1)%16));
            for (int j=0;j<16;++j)
            {
                b2Vec2 r=b2Body_GetPosition(rig_id(edge,j));
                b2Vec2 s=b2Body_GetPosition(rig_id(edge,(j+1)%16));
                if (b2Cross(b2Sub(q,p),b2Sub(r,p))*b2Cross(b2Sub(q,p),b2Sub(s,p)) < -1e-10f &&
                    b2Cross(b2Sub(s,r),b2Sub(p,r))*b2Cross(b2Sub(s,r),b2Sub(q,r)) < -1e-10f) return false;
            }
        }
    return true;
}

static float stack_contact_residual(dango_self_soft_body** bodies,int count,float skin)
{
    float residual=0;
    for (int a=0;a<count;++a) for (int b=a+1;b<count;++b)
        for (int pass=0;pass<2;++pass) for (int i=0;i<16;++i)
            residual=fmaxf(residual,evaluate_node_edge(bodies[pass ? b : a],i,bodies[pass ? a : b],skin,false));
    return residual;
}

static bool stack_solve(dango_self_soft_body** bodies,int count,
    const dango_self_physics_config* config,float skin)
{
    float contact_residual[4]={0},self_residual[4]={0};
    for (int iteration=0;iteration<96;++iteration)
    {
        stack_iterations_used=iteration+1;
        bool reverse=stack_order_mode==1 || (stack_order_mode==2 && ((iteration+stack_substep_index)%2!=0));
        double before_x[3]={0},before_px[3]={0};
        if (stack_trace) for (int k=0;k<count;++k)
        { before_x[k]=stack_mass_x(bodies[k],false); before_px[k]=stack_mass_x(bodies[k],true); }
        for (int a=0;a<count;++a) for (int b=a+1;b<count;++b)
            for (int pass=0;pass<2;++pass) for (int i=0;i<16;++i)
            {
                int first=reverse ? count-1-a : a,second=reverse ? count-1-b : b;
                int direction=pass;
                evaluate_node_edge(bodies[direction ? second : first],reverse ? 15-i : i,
                    bodies[direction ? first : second],skin,true);
            }
        if (stack_trace) for (int k=0;k<count;++k)
        {
            double x=stack_mass_x(bodies[k],false),px=stack_mass_x(bodies[k],true);
            stack_contact_dx[k]+=x-before_x[k]; stack_contact_px[k]+=px-before_px[k];
            before_x[k]=x; before_px[k]=px;
        }
        if (stack_residual_trace && (iteration+1)%24==0)
            contact_residual[iteration/24]=stack_contact_residual(bodies,count,skin);
        for (int k=0;k<count;++k) if (!pair_ground_solve(bodies[reverse ? count-1-k : k],config))
        { printf("solve_stop single_body=%d iteration=%d\n",reverse ? count-1-k : k,iteration); return false; }
        if (stack_residual_trace && (iteration+1)%24==0)
            self_residual[iteration/24]=stack_contact_residual(bodies,count,skin);
        if (stack_trace) for (int k=0;k<count;++k)
        { stack_self_dx[k]+=stack_mass_x(bodies[k],false)-before_x[k]; stack_self_px[k]+=stack_mass_x(bodies[k],true)-before_px[k]; }
        if (evaluation_max_momentum_error>1e-5f || evaluation_max_energy_gain>1e-5f ||
            evaluation_max_friction_budget_error>1e-7f) return false;
        if (stack_geometry(bodies,count,config,skin)) return true;
    }
    printf("solve_stop global_iterations=96\n");
    if (stack_residual_trace) for (int i=0;i<4;++i)
        printf("residual_iteration=%d after_contact=%.9g after_self=%.9g\n",(i+1)*24,
            (double)contact_residual[i],(double)self_residual[i]);
    return false;
}

static void stack_snapshot(dango_self_soft_body** bodies,int count,int frame,int sub,bool nodes)
{
    printf("snapshot frame=%d sub=%d",frame,sub);
    for (int k=0;k<count;++k)
    {
        b2Vec2 core=b2Body_GetPosition(bodies[k]->core_body_id);
        float low=INFINITY,high=-INFINITY,speed=0;
        for (int i=0;i<17;++i)
        {
            b2Vec2 p=b2Body_GetPosition(rig_id(bodies[k],i));
            if (i<16) { low=fminf(low,p.y); high=fmaxf(high,p.y); }
            speed=fmaxf(speed,b2Length(b2Body_GetLinearVelocity(rig_id(bodies[k],i))));
        }
        printf(" body%d=(x=%.9g,y=%.9g,height=%.9g,speed=%.9g)",k,
            (double)core.x,(double)core.y,(double)(high-low),(double)speed);
        if (stack_trace) printf(" core_vx%d=%.9g",k,(double)b2Body_GetLinearVelocity(bodies[k]->core_body_id).x);
    }
    printf("\n");
    if (nodes) for (int k=0;k<count;++k) for (int i=0;i<17;++i)
    {
        b2Vec2 p=b2Body_GetPosition(rig_id(bodies[k],i));
        b2Vec2 v=b2Body_GetLinearVelocity(rig_id(bodies[k],i));
        printf("node %d %d %.9g %.9g %.9g %.9g\n",k,i,(double)p.x,(double)p.y,(double)v.x,(double)v.y);
    }
}

static int stack_diagnostic(int count,float skin,bool check_support,float offset_ratio)
{
    if (count<2 || count>3 || (skin!=.02f && skin!=.045f)) return 9;
    if (!isfinite(offset_ratio) || fabsf(offset_ratio)>.1f) return 9;
    // The wider historical range remains diagnostic, not a required no-fall gate.
    if (check_support && fabsf(offset_ratio)>.05f) return 9;
    const char* residual_trace=getenv("DANGO_STACK_RESIDUAL_TRACE");
    if (residual_trace)
    {
        if (strcmp(residual_trace,"1")!=0) return 9;
        stack_residual_trace=true;
    }
    const char* continue_diagnostic=getenv("DANGO_STACK_CONTINUE_DIAGNOSTIC");
    if (continue_diagnostic)
    {
        if (strcmp(continue_diagnostic,"1")!=0) return 9;
        stack_continue_diagnostic=true;
    }
    const char* friction=getenv("DANGO_STACK_FRICTION");
    const char* relaxation=getenv("DANGO_STACK_POSITION_RELAXATION");
    if (relaxation)
    {
        if (strcmp(relaxation,"1")==0) evaluation_position_relaxation=1;
        else if (strcmp(relaxation,"1.25")==0) evaluation_position_relaxation=1.25f;
        else return 9;
        printf("DIAGNOSTIC position_relaxation=%.2f; correction cap and 96 iterations unchanged\n",
            (double)evaluation_position_relaxation);
    }
    if (friction)
    {
        char* end=NULL;
        evaluation_contact_mu=strtof(friction,&end);
        if (end==friction || *end!='\0' || !isfinite(evaluation_contact_mu) ||
            (evaluation_contact_mu!=0 && evaluation_contact_mu!=.3f)) return 9;
        printf("DIAGNOSTIC contact_mu=%.3f (per normal impulse, no contact cache)\n",(double)evaluation_contact_mu);
    }
    const char* order=getenv("DANGO_STACK_ORDER");
    if (order)
    {
        if (strcmp(order,"forward")==0) stack_order_mode=0;
        else if (strcmp(order,"reverse")==0) stack_order_mode=1;
        else if (strcmp(order,"alternating")==0) stack_order_mode=2;
        else return 9;
        stack_trace=true;
        printf("DIAGNOSTIC order=%s; mass-weighted phase deltas; contact_mu=%.3f\n",order,(double)evaluation_contact_mu);
    }
    ground_candidate=true; ground_variant=4; preserve_joint_impulses=true;
    dango_self_physics_config config=configuration();
    void* handle=NULL;
    CHECK(dango_box2d_self_soft_body_create(&config,&handle)==0);
    dango_self_soft_body extra[2]={{0},{0}};
    dango_self_soft_body* bodies[3]={handle,&extra[0],&extra[1]};
    // Fixed unloaded starting contour width; never recomputed under compression.
    b2Vec2 reference_nodes[16];
    b2Vec2 reference_core=b2Body_GetPosition(bodies[0]->core_body_id);
    for (int i=0;i<16;++i) reference_nodes[i]=b2Sub(b2Body_GetPosition(rig_id(bodies[0],i)),reference_core);
    float left=INFINITY,right=-INFINITY;
    for (int i=0;i<512;++i)
    {
        float x=contour_sample(reference_nodes,i).x;
        left=fminf(left,x); right=fmaxf(right,x);
    }
    float reference_width=right-left;
    printf("offset_ratio=%.9g reference_width=%.9g adjacent_dx=%.9g\n",
        (double)offset_ratio,(double)reference_width,(double)(offset_ratio*reference_width));
    for (int k=1;k<count;++k)
    {
        bodies[k]->world_id=bodies[0]->world_id;
        bodies[k]->ground_body_id=bodies[0]->ground_body_id;
        bodies[k]->ground_shape_id=bodies[0]->ground_shape_id;
        dango_self_create_rig(bodies[k],&config);
        for (int i=0;i<17;++i) b2Body_SetTransform(rig_id(bodies[k],i),
            b2Add(b2Body_GetPosition(rig_id(bodies[k],i)),(b2Vec2){offset_ratio*reference_width*k,1.65f*k}),b2Rot_identity);
    }
    b2Shape_SetFriction(bodies[0]->ground_shape_id,0);
    printf("DIAGNOSTIC count=%d skin=%.3f; load thresholds not yet fixed; no stack sleep\n",count,(double)skin);
    int result=0;
    for (int frame=0;frame<36000 && result==0;++frame)
    {
        for (int sub=0;sub<4;++sub)
        {
            stack_substep_index=frame*4+sub;
            for (int k=0;k<count;++k)
            {
                dango_self_update_joint_parameters(bodies[k],&config);
                dango_self_apply_internal_forces(bodies[k],&config);
            }
            double world_before_x[3]={0},world_before_px[3]={0};
            if (stack_trace) for (int k=0;k<count;++k)
            { world_before_x[k]=stack_mass_x(bodies[k],false); world_before_px[k]=stack_mass_x(bodies[k],true); }
            group_ground_step(bodies,count,1.0f/240.0f);
            if (stack_trace) for (int k=0;k<count;++k)
            { stack_world_dx[k]+=stack_mass_x(bodies[k],false)-world_before_x[k]; stack_world_px[k]+=stack_mass_x(bodies[k],true)-world_before_px[k]; }
            int projections_before=evaluation_projection_count;
            if (!stack_solve(bodies,count,&config,skin))
            {
                printf("FAIL joint/contact geometry convergence\n");
                for (int k=0;k<count;++k) printf("failure_legal body=%d legal=%d\n",k,legal(bodies[k],&config));
                for (int a=0;a<count;++a) for (int b=a+1;b<count;++b)
                {
                    float residual=0;
                    for (int pass=0;pass<2;++pass) for (int i=0;i<16;++i)
                        residual=fmaxf(residual,evaluate_node_edge(bodies[pass ? b : a],i,bodies[pass ? a : b],skin,false));
                    printf("failure_contact pair=%d,%d residual=%.9g\n",a,b,(double)residual);
                }
                stack_snapshot(bodies,count,frame,sub,true); result=1;
                if (stack_continue_diagnostic)
                {
                    bool converged=stack_solve(bodies,count,&config,skin);
                    printf("POST_FAILURE_DIAGNOSTIC converged=%d additional_iterations=%d residual=%.9g; original failure retained, no world step\n",
                        converged,stack_iterations_used,(double)stack_contact_residual(bodies,count,skin));
                }
                break;
            }
            if (stack_trace && projections_before==0 && evaluation_projection_count>0)
            { printf("first_contact frame=%d sub=%d\n",frame,sub); stack_snapshot(bodies,count,frame,sub,false); }
            float maximum_speed=0;
            for (int k=0;k<count;++k) for (int i=0;i<17;++i)
                maximum_speed=fmaxf(maximum_speed,b2Length(b2Body_GetLinearVelocity(rig_id(bodies[k],i))));
            if (maximum_speed>config.maximum_speed)
                for (int k=0;k<count;++k) for (int i=0;i<17;++i)
                    b2Body_SetLinearVelocity(rig_id(bodies[k],i),b2MulSV(config.maximum_speed/maximum_speed,
                        b2Body_GetLinearVelocity(rig_id(bodies[k],i))));
            for (int k=0;k<count;++k) if (!dango_self_limit_speed(bodies[k],config.maximum_speed)) result=1;
            if (result) { stack_snapshot(bodies,count,frame,sub,true); break; }
            // Falling all the way to the floor is already a decisive failure
            // for the user-approved centered/small-offset no-fall requirement.
            if (check_support) for (int k=1;k<count;++k) for (int i=0;i<16;++i)
                if (fabsf(b2Body_GetPosition(rig_id(bodies[k],i)).y-config.node_radius-config.ground_height)<.005f)
                {
                    if (!result) printf("FAIL upper reached floor layer=%d frame=%d sub=%d\n",k,frame,sub);
                    result=1;
                }
            if (result) { stack_snapshot(bodies,count,frame,sub,true); break; }
        }
        if (frame%600==0 || frame==35999) stack_snapshot(bodies,count,frame,3,false);
        if (stack_trace && (frame%30==0 || result))
        {
            stack_snapshot(bodies,count,frame,3,false);
            for (int k=0;k<count;++k)
                printf("phase frame=%d body=%d world_dx=%.9g contact_dx=%.9g self_dx=%.9g world_px=%.9g contact_px=%.9g self_px=%.9g\n",
                    frame,k,stack_world_dx[k],stack_contact_dx[k],stack_self_dx[k],stack_world_px[k],stack_contact_px[k],stack_self_px[k]);
        }
    }
    // A necessary support-path gate, not sufficient proof of load compression.
    // Keep statistical compression thresholds unset until a stack actually exists.
    if (check_support && result==0)
    {
        for (int k=1;k<count;++k)
        {
            dango_self_soft_body* adjacent[2]={bodies[k-1],bodies[k]};
            bool floor=false;
            for (int i=0;i<16;++i)
                floor |= fabsf(b2Body_GetPosition(rig_id(bodies[k],i)).y-config.node_radius-config.ground_height)<.005f;
            bool above=b2Body_GetPosition(bodies[k]->core_body_id).y>b2Body_GetPosition(bodies[k-1]->core_body_id).y;
            bool near=pair_near(adjacent,skin);
            printf("support layer=%d floor=%d above=%d adjacent_near=%d\n",k,floor,above,near);
            if (floor || !above || !near) result=1;
        }
        if (result) { printf("FAIL required vertical support path absent\n"); stack_snapshot(bodies,count,35999,3,true); }
    }
    int bytes=-1;
    if (dango_box2d_self_soft_body_destroy(handle,&bytes)!=0 || bytes!=0) result=1;
    printf("diagnostic_exit=%d resources=%d projections=%d\n",result,bytes,evaluation_projection_count);
    printf("contact_metrics momentum_error=%.9g energy_gain=%.9g friction_budget_error=%.9g\n",
        (double)evaluation_max_momentum_error,(double)evaluation_max_energy_gain,(double)evaluation_max_friction_budget_error);
    printf("friction_calls=%d saturated=%d summed_slip_before=%.9g after=%.9g\n",
        evaluation_friction_calls,evaluation_friction_saturated,evaluation_tangent_speed_before,evaluation_tangent_speed_after);
    return result;
}

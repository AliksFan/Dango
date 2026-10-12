#ifndef DANGO_TOUCHING_RECEIPT_H
#define DANGO_TOUCHING_RECEIPT_H
// A current touching receipt is retained, never retired or replayed. This
// classification is authenticated by live capture on every import and gate.
typedef struct dango_tr_view
{
    int pending_count;
    bool pending[16];
} dango_tr_view;
static inline bool dango_tr_qualify(const dango_rh_capture* c,const dango_material_ground_receipt* r,const dango_constraint_state* s)
{
    if(!c || !s || !r || r->node<0 || r->node>=16 || !dango_rh_outcone(r)) return false;
    const dango_rh_point* p=c->points+r->node;
    if(p->node!=r->node || !dango_rh_live(c,p) || p->delta_count!=1 || !isfinite(p->delta_n) || !(p->delta_n<0) || p->delta_t!=0 || p->friction_n<0 || p->rest_n<0 || p->stored_n<0 || p->friction<0) return false;
    bool ground_a=p->shape_a==c->ground.index1-1;
    float orientation=ground_a?1.0f:-1.0f;
    if(p->nx!=0 || p->ny!=orientation || r->normal_impulse!=(double)p->stored_n || r->tangent_impulse!=(double)p->stored_t || r->friction!=(double)p->friction) return false;
    double bound=(double)p->friction*p->friction_n,scale=fmax(bound,fabs((double)p->friction_t));
    if(fabs((double)p->friction_t)>bound+8*FLT_EPSILON*scale || p->delta_n!=(float)(p->rest_n-p->friction_n) || p->rest_n!=(float)(p->friction_n+p->delta_n) || p->rest_t!=p->friction_t || p->stored_n!=p->rest_n || p->stored_t!=p->rest_t) return false;
    double gap=(double)s->positions[r->node].y-(double)(s->ground+s->radius);
    return isfinite(gap) && gap==0;
}
static inline bool dango_tr_filter(dango_rh_capture* c,const dango_constraint_state* s,const dango_material_ground_receipt* r,int count,dango_rh_view* v,dango_tr_view* t)
{
    if(!v || !t || !s || !dango_mc_state_valid(s) || !dango_rh_raw_valid(r,count)) return false;
    memset(v,0,sizeof *v);memset(t,0,sizeof *t);v->source=c;v->sequence=c?c->sequence:0;v->input=*s;v->raw_count=count;
    if(c){v->step=c->step;v->world=c->world;v->ground=c->ground;memcpy(v->nodes,c->nodes,sizeof v->nodes);memcpy(v->bodies,c->bodies,sizeof v->bodies);memcpy(v->certificates,c->points,sizeof v->certificates);}
    for(int i=0;i<count;++i){
        v->raw[i]=r[i];
        if(!dango_rh_outcone(r+i))v->current[v->current_count++]=r[i];
        else if(dango_rh_qualify(c,r+i,s)){v->retired[r[i].node]=true;++v->history_count;}
        else if(dango_tr_qualify(c,r+i,s)){
            if(t->pending_count!=0)return false;
            t->pending[r[i].node]=true;++t->pending_count;v->current[v->current_count++]=r[i];
        }else return false;
    }
    return true;
}
static inline bool dango_tr_revalidate(const dango_rh_view* v,const dango_tr_view* t,const dango_material_ground_receipt* fresh,int count,const dango_constraint_state* s,const dango_material_contact_solver* solver)
{
    if(!v || !t || !s || !dango_rh_raw_valid(fresh,count) || count!=v->raw_count || t->pending_count<0 || t->pending_count>1)return false;
    if(v->history_count || t->pending_count){
        if(!v->source || v->sequence!=v->source->sequence || v->step!=v->source->step || v->world.index1!=v->source->world.index1 || v->world.generation!=v->source->world.generation || !B2_ID_EQUALS(v->ground,v->source->ground))return false;
        for(int n=0;n<16;++n)if(v->retired[n] || t->pending[n])if(!B2_ID_EQUALS(v->nodes[n],v->source->nodes[n]) || !B2_ID_EQUALS(v->bodies[n],v->source->bodies[n]) || memcmp(v->certificates+n,v->source->points+n,sizeof(dango_rh_point))!=0)return false;
    }
    for(int i=0;i<count;++i)if(!dango_rh_receipt_equal(fresh+i,v->raw+i))return false;
    dango_rh_view rebuilt;dango_tr_view classified;
    if(!dango_tr_filter(v->source,s,fresh,count,&rebuilt,&classified) || rebuilt.current_count!=v->current_count || rebuilt.history_count!=v->history_count || memcmp(rebuilt.retired,v->retired,sizeof v->retired)!=0 || memcmp(&classified,t,sizeof classified)!=0 || !dango_mc_same_geometry(&v->input,s))return false;
    for(int i=0;i<v->current_count;++i)if(!dango_rh_receipt_equal(rebuilt.current+i,v->current+i))return false;
    if(!solver)return true;
    if(!dango_mc_same_geometry(&solver->geometry,&v->input) || solver->receipt_count!=v->current_count)return false;
    for(int i=0;i<v->current_count;++i)if(!dango_rh_receipt_equal(solver->receipts+i,v->current+i))return false;
    for(int n=0;n<17;++n)if(solver->input_velocities[n].x!=v->input.velocities[n].x || solver->input_velocities[n].y!=v->input.velocities[n].y)return false;
    for(int n=0;n<16;++n){
        const dango_material_contact_support* support=solver->support+n;
        if(rebuilt.retired[n] && (support->receipt_present || support->touching || support->base_normal!=0 || support->base_tangent!=0 || support->friction!=0 || support->receipt_cone_slack!=0 || support->total_normal_telemetry!=0 || solver->rows[128+n].active || solver->rows[144+n].active || solver->rows[128+n].multiplier!=0 || solver->rows[144+n].multiplier!=0 || dango_mc_friction_bound(solver,n)!=0))return false;
        if(classified.pending[n]){
            const dango_material_ground_receipt* raw=NULL;for(int i=0;i<count;++i)if(fresh[i].node==n)raw=fresh+i;
            if(!raw || rebuilt.retired[n] || !support->receipt_present || !support->touching || !solver->rows[128+n].active || !solver->rows[144+n].active || support->base_normal!=raw->normal_impulse || support->base_tangent!=raw->tangent_impulse || support->friction!=raw->friction || support->total_normal_telemetry!=raw->total_normal_impulse || support->receipt_cone_slack!=0)return false;
        }
    }
    return true;
}
typedef struct dango_tr_context
{
    void* body;
    dango_rh_capture* (*source)(void*);
    bool (*receipts)(void*,dango_material_ground_receipt*,int*);
    const dango_rh_view* view;
    const dango_tr_view* classification;
    dango_rh_view saved;
    dango_tr_view saved_classification;
} dango_tr_context;
#if defined(_MSC_VER)
static __declspec(thread) dango_tr_context dango_tr_current;
#else
static _Thread_local dango_tr_context dango_tr_current;
#endif
static inline void dango_tr_end(void){memset(&dango_tr_current,0,sizeof dango_tr_current);}
static inline bool dango_tr_context_check(const dango_constraint_state* s,const dango_material_contact_solver* solver)
{
    const dango_tr_context* c=&dango_tr_current;dango_material_ground_receipt fresh[16];int count=0;
    if(!c->body || !c->source || !c->receipts || !c->view || !c->classification || c->source(c->body)!=c->saved.source || memcmp(c->view,&c->saved,sizeof c->saved)!=0 || memcmp(c->classification,&c->saved_classification,sizeof c->saved_classification)!=0 || !c->receipts(c->body,fresh,&count))return false;
    return dango_tr_revalidate(&c->saved,&c->saved_classification,fresh,count,s,solver);
}
static inline bool dango_tr_begin(void* body,dango_rh_capture* (*source)(void*),bool (*receipts)(void*,dango_material_ground_receipt*,int*),const dango_rh_view* v,const dango_tr_view* t)
{
    if(dango_tr_current.body || !body || !source || !receipts || !v || !t || source(body)!=v->source)return false;
    dango_tr_current.body=body;dango_tr_current.source=source;dango_tr_current.receipts=receipts;dango_tr_current.view=v;dango_tr_current.classification=t;dango_tr_current.saved=*v;dango_tr_current.saved_classification=*t;
    if(!dango_tr_context_check(&v->input,NULL)){dango_tr_end();return false;}return true;
}
// Declared immediately before the original solver's builder. It is consulted
// only for an actual out-of-cone receipt; ordinary import arithmetic is retained.
static inline bool dango_tr_import_receipt(const dango_constraint_state* s,const dango_material_ground_receipt* r,int count,int index)
{
    const dango_tr_context* c=&dango_tr_current;
    if(index<0 || index>=count || !r || r[index].node<0 || r[index].node>=16 || !c->body || c->saved_classification.pending_count!=1 || !c->saved_classification.pending[r[index].node] || count!=c->saved.current_count || memcmp(s,&c->saved.input,sizeof *s)!=0 || !dango_tr_context_check(s,NULL))return false;
    for(int i=0;i<count;++i)if(!dango_rh_receipt_equal(r+i,c->saved.current+i))return false;
    return true;
}
#endif

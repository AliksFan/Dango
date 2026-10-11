#ifndef DANGO_RECEIPT_HISTORY_RETIREMENT_H
#define DANGO_RECEIPT_HISTORY_RETIREMENT_H
#include "capture.h"
#include "dango_material_contact_block.h"
typedef struct dango_rh_view
{
    dango_rh_capture* source;
    uint64_t sequence,step;
    b2WorldId world;
    b2ShapeId ground,nodes[16];
    b2BodyId bodies[16];
    dango_rh_point certificates[16];
    dango_constraint_state input;
    dango_material_ground_receipt raw[16], current[16];
    int raw_count,current_count,history_count;
    bool retired[16];
} dango_rh_view;
static inline bool dango_rh_receipt_equal(const dango_material_ground_receipt* a,const dango_material_ground_receipt* b)
{ return a->node==b->node && a->normal_impulse==b->normal_impulse && a->tangent_impulse==b->tangent_impulse && a->friction==b->friction && a->total_normal_impulse==b->total_normal_impulse; }
static inline bool dango_rh_raw_valid(const dango_material_ground_receipt* r,int count)
{
    if(count<0 || count>16 || (count && !r)) return false;
    bool nodes[16]={false};
    for(int i=0;i<count;++i) {
        if(r[i].node<0 || r[i].node>=16 || nodes[r[i].node] || !isfinite(r[i].normal_impulse) || !isfinite(r[i].tangent_impulse) || !isfinite(r[i].friction) || !isfinite(r[i].total_normal_impulse) || r[i].normal_impulse<0 || r[i].friction<0 || r[i].total_normal_impulse<0) return false;
        nodes[r[i].node]=true;
    }
    return true;
}
static inline bool dango_rh_outcone(const dango_material_ground_receipt* r)
{
    double bound=r->friction*r->normal_impulse,scale=fmax(fabs(r->tangent_impulse),bound);
    return !isfinite(bound) || fabs(r->tangent_impulse)>bound+8*FLT_EPSILON*scale;
}
static inline bool dango_rh_qualify(const dango_rh_capture* c,const dango_material_ground_receipt* r,const dango_constraint_state* s)
{
    if(!c || !dango_rh_outcone(r)) return false;
    const dango_rh_point* p=c->points+r->node;
    if(p->node!=r->node || !dango_rh_live(c,p) || p->delta_count!=1 || !isfinite(p->delta_n) || !(p->delta_n<0) || p->delta_t!=0 || p->friction_n<0 || p->rest_n<0 || p->stored_n<0 || p->friction<0) return false;
    bool ground_a=p->shape_a==c->ground.index1-1;
    float orientation=ground_a?1.0f:-1.0f;
    if(p->nx!=0 || p->ny!=orientation || r->normal_impulse!=(double)p->stored_n || r->tangent_impulse!=(double)p->stored_t || r->friction!=(double)p->friction) return false;
    double bound=(double)p->friction*p->friction_n,scale=fmax(bound,fabs((double)p->friction_t));
    if(fabs((double)p->friction_t)>bound+8*FLT_EPSILON*scale || p->delta_n!=(float)(p->rest_n-p->friction_n) || p->rest_n!=(float)(p->friction_n+p->delta_n) || p->rest_t!=p->friction_t || p->stored_n!=p->rest_n || p->stored_t!=p->rest_t) return false;
    double gap=(double)s->positions[r->node].y-(double)(s->ground+s->radius);
    double tolerance=(double)DANGO_CONSTRAINT_TOLERANCE*(double)s->radial_length;
    return isfinite(gap) && gap>tolerance;
}
static inline bool dango_rh_filter(dango_rh_capture* c,const dango_constraint_state* s,const dango_material_ground_receipt* r,int count,dango_rh_view* v)
{
    if(!v || !s || !dango_mc_state_valid(s) || !dango_rh_raw_valid(r,count)) return false;
    memset(v,0,sizeof *v); v->source=c; v->sequence=c?c->sequence:0; v->input=*s; v->raw_count=count;
    if(c) { v->step=c->step; v->world=c->world; v->ground=c->ground; memcpy(v->nodes,c->nodes,sizeof v->nodes); memcpy(v->bodies,c->bodies,sizeof v->bodies); memcpy(v->certificates,c->points,sizeof v->certificates); }
    for(int i=0;i<count;++i) {
        v->raw[i]=r[i];
        if(!dango_rh_outcone(r+i)) v->current[v->current_count++]=r[i];
        else {
            if(!dango_rh_qualify(c,r+i,s)) return false;
            v->retired[r[i].node]=true; ++v->history_count;
        }
    }
    return true;
}
// Recompute the classification and canonical support list, never trust flags.
static inline bool dango_rh_revalidate(const dango_rh_view* v,const dango_material_ground_receipt* fresh,int count,const dango_constraint_state* s,const dango_material_contact_solver* solver)
{
    if(!v || !s || !dango_rh_raw_valid(fresh,count) || count!=v->raw_count || (v->history_count && (!v->source || v->sequence!=v->source->sequence || v->step!=v->source->step || v->world.index1!=v->source->world.index1 || v->world.generation!=v->source->world.generation || !B2_ID_EQUALS(v->ground,v->source->ground)))) return false;
    if(v->history_count) for(int n=0;n<16;++n) if(v->retired[n] && (!B2_ID_EQUALS(v->nodes[n],v->source->nodes[n]) || !B2_ID_EQUALS(v->bodies[n],v->source->bodies[n]) || memcmp(v->certificates+n,v->source->points+n,sizeof(dango_rh_point))!=0)) return false;
    for(int i=0;i<count;++i) {
        const dango_material_ground_receipt *a=fresh+i,*b=v->raw+i;
        if(!dango_rh_receipt_equal(a,b)) return false;
    }
    dango_rh_view rebuilt;
    if(!dango_rh_filter(v->source,s,fresh,count,&rebuilt) || rebuilt.current_count!=v->current_count || rebuilt.history_count!=v->history_count || memcmp(rebuilt.retired,v->retired,sizeof v->retired)!=0 || !dango_mc_same_geometry(&v->input,s)) return false;
    for(int i=0;i<v->current_count;++i) if(!dango_rh_receipt_equal(&rebuilt.current[i],&v->current[i])) return false;
    if(!solver) return true;
    if(!dango_mc_same_geometry(&solver->geometry,&v->input) || solver->receipt_count!=v->current_count) return false;
    for(int i=0;i<v->current_count;++i) if(!dango_rh_receipt_equal(&solver->receipts[i],&v->current[i])) return false;
    for(int n=0;n<17;++n) if(solver->input_velocities[n].x!=v->input.velocities[n].x || solver->input_velocities[n].y!=v->input.velocities[n].y) return false;
    for(int n=0;n<16;++n) if(rebuilt.retired[n]) {
        const dango_material_contact_support* support=solver->support+n;
        if(support->receipt_present || support->touching || support->base_normal!=0 || support->base_tangent!=0 || support->friction!=0 || support->receipt_cone_slack!=0 || support->total_normal_telemetry!=0 || solver->rows[128+n].active || solver->rows[144+n].active || solver->rows[128+n].multiplier!=0 || solver->rows[144+n].multiplier!=0 || dango_mc_friction_bound(solver,n)!=0) return false;
    }
    return true;
}
#endif

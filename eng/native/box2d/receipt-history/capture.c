#include "capture.h"
#include "world.h"
#include "shape.h"
#include "body.h"
#include "contact.h"
#include <math.h>
#include <string.h>

static bool same_float(float a,float b) { return memcmp(&a,&b,sizeof a)==0; }
dango_rh_capture* dango_rh_get(b2WorldId id)
{
    if(!b2World_IsValid(id)) return NULL;
    return b2GetWorldFromId(id)->dangoReceiptObserver;
}
bool dango_rh_begin(dango_rh_capture* c,b2WorldId id,b2ShapeId ground,const b2ShapeId nodes[16],const b2BodyId bodies[16])
{
    if(!c || !b2World_IsValid(id) || !b2Shape_IsValid(ground) || ground.world0!=id.index1-1) return false;
    b2World* w=b2GetWorldFromId(id);
    if(w->workerCount!=1 || (w->dangoReceiptObserver && w->dangoReceiptObserver!=c)) return false;
    uint64_t seq=c->sequence; if(seq==UINT64_MAX || w->stepIndex==UINT64_MAX) return false;
    memset(c,0,sizeof *c); c->sequence=seq+1; c->world=id; c->step=w->stepIndex+1; c->ground=ground;
    for(int n=0;n<16;++n) {
        if(!b2Shape_IsValid(nodes[n]) || !b2Body_IsValid(bodies[n]) || nodes[n].world0!=id.index1-1 || bodies[n].world0!=id.index1-1) return false;
        c->nodes[n]=nodes[n]; c->bodies[n]=bodies[n]; c->points[n].stage=0;
    }
    c->bound=true; w->dangoReceiptObserver=c; return true;
}
void dango_rh_end(dango_rh_capture* c)
{
    if(!c) return;
    if(b2World_IsValid(c->world)) { b2World* w=b2GetWorldFromId(c->world); if(w->dangoReceiptObserver==c) w->dangoReceiptObserver=NULL; }
    c->bound=false;
}
static bool identity(const b2World* w,const dango_rh_capture* c,const dango_rh_point* p)
{
    if(!c->bound || c->invalid || w->dangoReceiptObserver!=c || w->generation!=c->world.generation || w->worldId!=c->world.index1-1 || w->stepIndex!=c->step || p->sequence!=c->sequence || p->step!=c->step || p->node<0 || p->node>=16 || p->invalid) return false;
    if(p->shape_a<0 || p->shape_b<0 || p->shape_a>=w->shapes.count || p->shape_b>=w->shapes.count || p->body_a<0 || p->body_b<0 || p->body_a>=w->bodies.count || p->body_b>=w->bodies.count) return false;
    const b2Shape *a=w->shapes.data+p->shape_a,*b=w->shapes.data+p->shape_b;
    return a->generation==p->shape_gen_a && b->generation==p->shape_gen_b && a->bodyId==p->body_a && b->bodyId==p->body_b && w->bodies.data[p->body_a].generation==p->body_gen_a && w->bodies.data[p->body_b].generation==p->body_gen_b;
}
void dango_rh_event(b2World* w,const b2ContactSim* s,int point,int stage,float n,float t,float friction,float nx,float ny,float dn,float dt)
{
    dango_rh_capture* c=w->dangoReceiptObserver; if(!c || !c->bound) return;
    int node=-1;
    for(int i=0;i<16;++i) if((s->shapeIdA==c->ground.index1-1 && s->shapeIdB==c->nodes[i].index1-1) || (s->shapeIdB==c->ground.index1-1 && s->shapeIdA==c->nodes[i].index1-1)) { node=i; break; }
    if(node<0) return;
    dango_rh_point* p=c->points+node;
    // First candidate intentionally supports only a single point manifold.
    if(s->manifold.pointCount!=1 || point!=0 || !isfinite(n+t+friction+nx+ny+dn+dt) || n<0 || friction<0) { p->invalid=true; return; }
    if(stage==21) {
        if(p->invalid || (p->stage && (p->contact!=s->contactId || p->shape_a!=s->shapeIdA || p->shape_b!=s->shapeIdB || p->point_id!=s->manifold.points[point].id))) { p->invalid=true; return; }
        memset(p,0,sizeof *p); p->node=node; p->contact=s->contactId; p->point=point; p->point_id=s->manifold.points[point].id;
        p->shape_a=s->shapeIdA; p->shape_b=s->shapeIdB;
        const b2Shape* a=w->shapes.data+p->shape_a; const b2Shape* b=w->shapes.data+p->shape_b;
        p->body_a=a->bodyId; p->body_b=b->bodyId; p->shape_gen_a=a->generation; p->shape_gen_b=b->generation;
        p->body_gen_a=w->bodies.data[p->body_a].generation; p->body_gen_b=w->bodies.data[p->body_b].generation;
        p->sequence=c->sequence; p->step=c->step; p->nx=nx; p->ny=ny; p->friction=friction; p->friction_n=n; p->friction_t=t; p->stage=21; return;
    }
    if(!identity(w,c,p) || p->contact!=s->contactId || p->point!=point || p->point_id!=s->manifold.points[point].id || p->shape_a!=s->shapeIdA || p->shape_b!=s->shapeIdB || !same_float(p->friction,friction) || !same_float(p->nx,nx) || !same_float(p->ny,ny)) { p->invalid=true; return; }
    if(stage==30 && p->stage==21 && same_float(n,p->friction_n) && same_float(t,p->friction_t)) p->stage=30;
    else if(stage==35 && p->stage==30 && p->delta_count==0 && same_float(n,p->friction_n) && same_float(t,p->friction_t)) { p->delta_n=dn; p->delta_t=dt; p->delta_count=1; }
    else if(stage==31 && p->stage==30) { p->rest_n=n; p->rest_t=t; p->stage=31; }
    else if(stage==40 && p->stage==31 && same_float(n,p->rest_n) && same_float(t,p->rest_t)) p->stage=40;
    else if(stage==41 && p->stage==40 && same_float(n,p->rest_n) && same_float(t,p->rest_t) && same_float(n,s->manifold.points[point].normalImpulse) && same_float(t,s->manifold.points[point].tangentImpulse)) { p->stored_n=n; p->stored_t=t; p->stage=41; }
    else p->invalid=true;
}
bool dango_rh_live(const dango_rh_capture* c,const dango_rh_point* p)
{
    if(!c || !p || !b2World_IsValid(c->world)) return false;
    b2World* w=b2GetWorldFromId(c->world); if(!identity(w,c,p) || p->stage!=41 || p->point!=0 || p->contact<0 || p->contact>=w->contacts.count) return false;
    const b2Contact* contact=w->contacts.data+p->contact;
    if(contact->contactId!=p->contact || contact->shapeIdA!=p->shape_a || contact->shapeIdB!=p->shape_b) return false;
    b2ContactSim* sim=b2GetContactSim(w,(b2Contact*)contact);
    if(!sim || sim->manifold.pointCount!=1 || sim->manifold.points[0].id!=p->point_id || !same_float(sim->friction,p->friction) || !same_float(sim->manifold.normal.x,p->nx) || !same_float(sim->manifold.normal.y,p->ny) || !same_float(sim->manifold.points[0].normalImpulse,p->stored_n) || !same_float(sim->manifold.points[0].tangentImpulse,p->stored_t)) return false;
    bool ground_a=p->shape_a==c->ground.index1-1;
    b2ShapeId node=c->nodes[p->node]; b2BodyId body=c->bodies[p->node];
    if(c->ground.world0!=c->world.index1-1 || node.world0!=c->world.index1-1 || body.world0!=c->world.index1-1 || !b2Shape_IsValid(c->ground) || !b2Shape_IsValid(node) || !b2Body_IsValid(body)) return false;
    if(!ground_a && p->shape_b!=c->ground.index1-1) return false;
    if((ground_a?p->shape_gen_a:p->shape_gen_b)!=c->ground.generation) return false;
    return (ground_a?p->shape_b:p->shape_a)==node.index1-1 && (ground_a?p->body_b:p->body_a)==body.index1-1;
}

#include "dango_self_physics_internal.h"
#include <string.h>
static b2BodyId detailIds[17];
static b2Vec2 detailForces[17];
static unsigned detailMask;
static void detail_capture(b2BodyId id,b2Vec2 force) {
    for(int i=0;i<17;++i)if(!memcmp(&id,&detailIds[i],sizeof id)) {
        detailForces[i]=b2Add(detailForces[i],force);detailMask|=1u<<i;
    }
}
#define DANGO_LAB_CAPTURE_DETAIL detail_capture
#define main legacy_main
#include "dango_physics_lab_tests.c"
#undef main
#include <windows.h>
int dango_physics_lab_region_version(void);
int dango_physics_world_get_lab_region_stats(void*,int,float*,int);

static void clear_capture(void) {
    capturedX=capturedY=capturedTorque=capturedNodeMagnitude=0;capturedCalls=0;
    detailMask=0;memset(detailForces,0,sizeof detailForces);
}
static int regions_contract(void) {
    b2WorldDef wd=b2DefaultWorldDef();wd.gravity=(b2Vec2){0};
    b2WorldId world=b2CreateWorld(&wd);dango_self_soft_body body={0};body.node_count=16;body.grounded=true;
    b2Vec2 p[17];double masses[17],cx=0,cy=0,total=0;
    for(int i=0;i<17;++i) {
        double a=6.283185307179586*(i-1)/16;
        p[i]=i==0 ? (b2Vec2){3,4} : (b2Vec2){(float)(3+.68*cos(a)),(float)(4+.68*sin(a))};
        b2BodyDef bd=b2DefaultBodyDef();bd.type=b2_dynamicBody;bd.position=p[i];
        b2BodyId id=b2CreateBody(world,&bd);b2ShapeDef sd=b2DefaultShapeDef();sd.density=1+(float)i*.1f;
        detailIds[i]=id;
        b2Circle circle={{0},.05f};b2CreateCircleShape(id,&sd,&circle);
        if(i==0)body.core_body_id=id;else body.node_body_ids[i-1]=id;
        masses[i]=b2Body_GetMass(id);total+=masses[i];cx+=masses[i]*p[i].x;cy+=masses[i]*p[i].y;
    }
    cx/=total;cy/=total;captureCore=body.core_body_id;captureOrigin=(b2Vec2){(float)cx,(float)cy};
    float bounds[4];CHECK(dango_region_bounds(p,config.node_radius,bounds));
    /* Independent dense Bernstein oracle, no analytic extrema helper. */
    double oracle[4]={INFINITY,INFINITY,-INFINITY,-INFINITY};
    for(int i=0;i<16;++i)for(int k=0;k<=2000;++k) {
        double t=k/2000.0,w[3]={.5*(1-t)*(1-t),.5+t-t*t,.5*t*t},x=0,y=0;
        for(int j=0;j<3;++j){int n=(i+15+j)%16+1;double dx=p[n].x-p[0].x,dy=p[n].y-p[0].y,l=hypot(dx,dy);
            x+=w[j]*(p[n].x+config.node_radius*dx/l);y+=w[j]*(p[n].y+config.node_radius*dy/l);}
        oracle[0]=fmin(oracle[0],x);oracle[1]=fmin(oracle[1],y);oracle[2]=fmax(oracle[2],x);oracle[3]=fmax(oracle[3],y);
    }
    for(int i=0;i<4;++i)CHECK(fabs(bounds[i]-oracle[i])<1e-6);
    /* An exact equal-distance pair must choose the earlier contour index. */
    b2Vec2 tied[17]={0};float tiedBounds[4]={0,-1,1.5f,1};
    for(int i=1;i<17;++i)tied[i]=(b2Vec2){0,0};
    tied[1]=(b2Vec2){1.25f,.125f};tied[2]=(b2Vec2){1.25f,-.125f};
    CHECK(dango_region_select(tied,tiedBounds,1,true)==1u);
    /* A changed physical position changes membership immediately. */
    unsigned before=dango_region_select(p,bounds,1,false);b2Vec2 changed[17];memcpy(changed,p,sizeof p);
    changed[1].x=bounds[0];CHECK(dango_region_select(changed,bounds,1,false)==(before&~1u));
    for(int mode=4;mode<=5;++mode)for(int dir=-1;dir<=1;++dir) {
        dango_lab_state s={0};s.mode=mode;float c[6]={0};c[0]=(float)dir;
        if(dir==0)s.stretch=s.ramp=1;
        clear_capture();CHECK(!dango_lab_forces(&body,&config,&s,.001f,c));
        float expected=dir==0 ? config.downward_force : config.horizontal_force;
        CHECK(fabs(hypot(capturedX,capturedY)-expected)<1e-6);
        CHECK(dir!=0 || capturedY>0);CHECK(dir==0 || fabs(capturedY)<1e-8);
        CHECK(fabs(capturedTorque-s.region_stats[12])<1e-6);
        unsigned mask=(unsigned)s.region_stats[dir==0 ? 5 : 4];int count=0;
        for(int i=0;i<16;++i)if(mask&(1u<<i)) {
            ++count;CHECK(dir<0 ? p[i+1].x<=bounds[0]+(bounds[2]-bounds[0])/3 :
                dir>0 ? p[i+1].x>=bounds[2]-(bounds[2]-bounds[0])/3 : p[i+1].y>=bounds[3]-(bounds[3]-bounds[1])/3);
        }
        CHECK(count==capturedCalls && (mode!=5 || count==1));
        CHECK(detailMask==(mask<<1));
        if(mode==5) {
            double best=INFINITY;int nearest=-1;
            double tx=(bounds[0]+bounds[2])*.5,ty=(bounds[1]+bounds[3])*.5;
            if(dir<0)tx=bounds[0]+(bounds[2]-bounds[0])/6;
            else if(dir>0)tx=bounds[2]-(bounds[2]-bounds[0])/6;
            else ty=bounds[3]-(bounds[3]-bounds[1])/6;
            for(int i=0;i<16;++i) {
                bool member=dir<0 ? p[i+1].x<=bounds[0]+(bounds[2]-bounds[0])/3 :
                    dir>0 ? p[i+1].x>=bounds[2]-(bounds[2]-bounds[0])/3 : p[i+1].y>=bounds[3]-(bounds[3]-bounds[1])/3;
                double dx=p[i+1].x-tx,dy=p[i+1].y-ty,d=dx*dx+dy*dy;
                if(member && d<best){nearest=i;best=d;}
            }
            CHECK(mask==(1u<<nearest));
        }
        double selectedMass=0;
        for(int i=0;i<16;++i)if(mask&(1u<<i))selectedMass+=masses[i+1];
        for(int i=0;i<16;++i)if(mask&(1u<<i)) {
            double expectedNode=expected*masses[i+1]/selectedMass;
            CHECK(fabs(hypot(detailForces[i+1].x,detailForces[i+1].y)-expectedNode)<1e-6);
        }
        clear_capture();s.cancel=1;CHECK(!dango_lab_forces(&body,&config,&s,.01f,c));CHECK(!capturedCalls && s.ramp==0);
    }
    /* Off-axis W has a nonzero core moment in both modes, including when A/D
       contributes an independent moment. Capture the actual per-body forces. */
    b2Vec2 shiftedCore=p[0];shiftedCore.x+=.1f;
    b2Body_SetTransform(body.core_body_id,shiftedCore,b2Rot_identity);
    for(int mode=4;mode<=5;++mode) {
        dango_lab_state torqueState={0};torqueState.mode=mode;torqueState.ramp=torqueState.stretch=1;
        float mixed[6]={1};clear_capture();
        CHECK(!dango_lab_forces(&body,&config,&torqueState,.001f,mixed));
        double actualWTorque=0;
        for(int i=1;i<=16;++i)actualWTorque+=(p[i].x-shiftedCore.x)*detailForces[i].y;
        CHECK(fabs(actualWTorque)>1e-4 && fabs(actualWTorque-torqueState.stats[12])<1e-6);
        CHECK(fabs(torqueState.stats[11]-config.downward_force)<1e-6);
    }
    dango_lab_state s={0};s.mode=4;float c[6]={0,1};clear_capture();
    CHECK(!dango_lab_forces(&body,&config,&s,.001f,c));CHECK(fabs(capturedY+2.2)<1e-6 && s.region_stats[5]>0);
    CHECK(s.stats[12]==0); /* S is not mislabeled as W torque. */
    s.ramp=.7f;s.stretch=0;clear_capture();
    CHECK(!dango_lab_forces(&body,&config,&s,.001f,c));CHECK(fabs(capturedY+2.2)<1e-6 && s.ramp==0);
    c[1]=0;clear_capture();CHECK(!dango_lab_forces(&body,&config,&s,.001f,c));CHECK(!capturedCalls && s.ramp==0);
    c[1]=1;
    s.jump_accepted=true;s.ramp=.4f;s.stretch=1;clear_capture();
    CHECK(!dango_lab_forces(&body,&config,&s,.01f,c));CHECK(!capturedCalls && s.ramp==.4f);
    s.jump_accepted=false;c[1]=0;clear_capture();CHECK(!dango_lab_forces(&body,&config,&s,.01f,c));
    CHECK(capturedY>0 && s.ramp>.4f);
    b2Body_SetLinearVelocity(body.core_body_id,(b2Vec2){config.maximum_horizontal_speed,0});
    s.ramp=s.stretch=0;c[0]=1;clear_capture();CHECK(!dango_lab_forces(&body,&config,&s,.01f,c));CHECK(!capturedCalls);
    b2DestroyWorld(world);return 0;
}
static int real_scenarios(void) {
    for(int mode=4;mode<=5;++mode) {
        void* w=NULL;int id=0;CHECK(!dango_physics_world_create(&config,1,&w));CHECK(!dango_physics_world_add(w,0,2,&id));
        float c[9]={0},state[42],stats[16];
        for(int f=0;f<900;++f) {
            memset(c,0,sizeof c);c[0]=f>=300&&f<390 ? 1.0f : f>=450&&f<540 ? -1.0f : 0.0f;
            c[6]=f>=600&&f<630 ? 1.0f : 0.0f;c[1]=f>=690&&f<720 ? 1.0f : 0.0f;c[2]=f==780 ? 1.0f : 0.0f;
            int result=step(w,id,c,mode,0,state,stats);
            if(result){fprintf(stderr,"real scenario mode %d frame %d FAILED\n",mode,f);return 1;}
            for(int j=0;j<42;++j)CHECK(isfinite(state[j]));
        }
        CHECK(!dango_physics_world_destroy(w));
    }
    for(int mode=4;mode<=5;++mode) {
        void* a=NULL;void* b=NULL;int ia=0,ib=0;
        CHECK(!dango_physics_world_create(&config,1,&a) && !dango_physics_world_create(&config,1,&b));
        CHECK(!dango_physics_world_add(a,0,2,&ia) && !dango_physics_world_add(b,0,2,&ib));
        float c[9]={0},d[9]={0},sa[42],sb[42],stats[16];
        for(int f=0;f<300;++f){CHECK(!step(a,ia,c,mode,0,sa,stats));CHECK(!step(b,ib,c,mode,0,sb,stats));}
        d[1]=d[2]=d[6]=1;c[2]=1;
        CHECK(!step(a,ia,c,mode,0,sa,stats));CHECK(!step(b,ib,d,mode,0,sb,stats));
        CHECK(!memcmp(sa,sb,sizeof sa) && stats[2]==0 && stats[9]==0);
        CHECK(!dango_physics_world_reset(b));memset(d,0,sizeof d);d[2]=d[6]=1;
        CHECK(!step(b,ib,d,mode,0,sb,stats));CHECK(stats[2]>0 && stats[5]>0);
        CHECK(!dango_physics_world_destroy(a) && !dango_physics_world_destroy(b));
    }
    return 0;
}
typedef int (*create_fn)(const dango_self_physics_config*,int,void**);
typedef int (*add_fn)(void*,float,float,int*);
typedef int (*destroy_fn)(void*);
typedef int (*labstep_fn)(void*,float,const int*,const float*,int,int,float,int*,float*,int,int*,int*,int*);
typedef struct parity_api { HMODULE dll;create_fn create;add_fn add;destroy_fn destroy;labstep_fn step; } parity_api;
static int load_parity(const char* path,parity_api* api) {
    api->dll=LoadLibraryA(path);CHECK(api->dll);
    FARPROC p=GetProcAddress(api->dll,"dango_physics_world_create");memcpy(&api->create,&p,sizeof p);CHECK(api->create);
    p=GetProcAddress(api->dll,"dango_physics_world_add");memcpy(&api->add,&p,sizeof p);CHECK(api->add);
    p=GetProcAddress(api->dll,"dango_physics_world_destroy");memcpy(&api->destroy,&p,sizeof p);CHECK(api->destroy);
    p=GetProcAddress(api->dll,"dango_physics_world_step_lab");memcpy(&api->step,&p,sizeof p);CHECK(api->step);return 0;
}
static int binary_parity(const char* oldPath,const char* newPath) {
    parity_api a={0},b={0};CHECK(!load_parity(oldPath,&a) && !load_parity(newPath,&b));
    int maximumMode=GetProcAddress(a.dll,"dango_physics_lab_region_version") ? 5 : 3;
    for(int mode=0;mode<=maximumMode;++mode) {
        void* wa=NULL;void* wb=NULL;int ia=0,ib=0;
        CHECK(!a.create(&config,1,&wa) && !b.create(&config,1,&wb));
        CHECK(!a.add(wa,0,2,&ia) && !b.add(wb,0,2,&ib));
        for(int frame=0;frame<720;++frame) {
            float c[9]={0},sa[42],sb[42];int ida=0,idb=0,na=0,nb=0,ca=0,cb=0,ea=0,eb=0;
            c[2]=frame==420 ? 1.0f : 0.0f;
            if(mode>=4) {
                c[0]=frame>=100&&frame<160 ? 1.0f : frame>=200&&frame<260 ? -1.0f : 0.0f;
                c[6]=frame>=300&&frame<330 ? 1.0f : 0.0f;
                c[1]=frame>=350&&frame<370 ? 1.0f : 0.0f;
            }
            CHECK(!a.step(wa,1.0f/60,&ia,c,1,mode,.4f,&ida,sa,42,&na,&ca,&ea));
            CHECK(!b.step(wb,1.0f/60,&ib,c,1,mode,.4f,&idb,sb,42,&nb,&cb,&eb));
            if(ea || eb || memcmp(sa,sb,sizeof sa) || na!=nb || ca!=cb) {
                fprintf(stderr,"binary parity mode %d frame %d failed errors %d/%d\n",mode,frame,ea,eb);return 1;
            }
        }
        CHECK(!a.destroy(wa) && !b.destroy(wb));
    }
    for(int mode=4;mode<=5;++mode) {
        void* w=NULL;int id=0;CHECK(!b.create(&config,1,&w));CHECK(!b.add(w,0,2,&id));
        for(int f=0;f<900;++f) {
            float c[9]={0},s[42];int sid=0,n=0,contacts=0,error=0;
            c[0]=f>=300&&f<390 ? 1.0f : f>=450&&f<540 ? -1.0f : 0.0f;
            c[6]=f>=600&&f<630 ? 1.0f : 0.0f;c[1]=f>=690&&f<720 ? 1.0f : 0.0f;c[2]=f==780 ? 1.0f : 0.0f;
            int result=b.step(w,1.0f/60,&id,c,1,mode,0,&sid,s,42,&n,&contacts,&error);
            if(result || error){fprintf(stderr,"DLL %s region mode %d frame %d result %d error %d FAILED\n",newPath,mode,f,result,error);return 1;}
        }
        CHECK(!b.destroy(w));
    }
    FreeLibrary(a.dll);FreeLibrary(b.dll);
    printf("old/new DLL modes 0..%d, 720 frames each (region modes include A/D/W/S), bitwise physics parity PASS\n",maximumMode);return 0;
}
int main(int argc,char** argv) {
    CHECK(dango_physics_lab_region_version()==1);
    CHECK(!legacy_main());CHECK(!regions_contract());CHECK(!real_scenarios());
    if(argc==5){CHECK(!binary_parity(argv[1],argv[2]));CHECK(!binary_parity(argv[3],argv[4]));}
    puts("direction regions: actual force capture, analytic bounds oracle, total force/torque, point membership, W/S, cancel/speed/jump gating, 900-frame scenarios PASS");
    return 0;
}

#include <stdio.h>
#include <math.h>
#include <string.h>
#include <windows.h>
#include "dango_physics_module.h"
#define DANGO_LAB_ENVELOPE
#include "dango_self_constraints.h"
int dango_physics_lab_ground_model(void);
int dango_collision_create(const dango_self_physics_config*,int,int,void**);
int dango_physics_lab_test_collapse(void*);
int dango_physics_world_prepare_furniture_change(void*,int,unsigned long long,unsigned long long,const void*,void*,int,void*);
int dango_physics_world_prepare_static_platform_change(void*,int,unsigned long long,unsigned long long,const void*,void*,int,void*);
int dango_physics_world_get_lab_ground_stats(void*,int,float*,int);
int dango_physics_world_step_lab(void*,float,const int*,const float*,int,int,float,int*,float*,int,int*,int*,int*);
#define CHECK(c) do {if(!(c)){fprintf(stderr,"FAIL %d: %s\n",__LINE__,#c);return 1;}}while(0)
    static dango_self_physics_config config={
        .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
        .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
        .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.ground_deformation_strength=0,
        .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,.core_exclusion_ratio=.25f,
        .angle_strength=10,.local_angle_balance_strength=1.7f,.top_inward_force=0,.gravity=9.8f,.core_mass=.12f,.node_mass=.015f,
        .drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,.contact_hertz=28,
        .linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
        .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,
        .jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f
    };
static b2Vec2 material_point(const b2Vec2* p,float r,int segment,float t) {
    float w[3]={.5f*(1-t)*(1-t),.5f+t-t*t,.5f*t*t};b2Vec2 result={0};
    for(int k=0;k<3;++k){int i=(segment+15+k)%16;b2Vec2 d=b2Sub(p[i],p[16]);result=b2Add(result,b2MulSV(w[k],b2Add(p[i],b2MulSV(r/b2Length(d),d))));}return result;
}
static dango_constraint_state state(void){
    dango_constraint_state s={0};s.radius=.045f;s.radial_length=.68f;s.perimeter_length=2*.68f*sinf(3.14159265358979323846f/16);
    s.radial_min=.65f;s.radial_max=1.22f;s.perimeter_min=.73f;s.perimeter_max=1.07f;s.ground=-.72f;s.envelope_friction=.72f;
    for(int i=0;i<17;++i){float a=6.283185307179586f*((float)i/16+.013f);s.positions[i]=i==16?(b2Vec2){.3f,.2f}:(b2Vec2){.3f+.68f*cosf(a),.2f+.68f*sinf(a)};s.inverse_mass[i]=i==16?1/.12f:1/.015f;}return s;
}
static int geometry_contract(void){
    dango_constraint_state s=state();dango_envelope_point p;CHECK(dango_envelope_geometry(s.positions,s.radius,&p));
    float dense=INFINITY,maxy=-INFINITY;
    for(int i=0;i<16;++i){b2Vec2 seam=material_point(s.positions,s.radius,i,1),next=material_point(s.positions,s.radius,(i+1)%16,0);CHECK(b2Distance(seam,next)<1e-7f);
        b2Vec2 derivA=b2MulSV(1000,b2Sub(seam,material_point(s.positions,s.radius,i,.999f))),derivB=b2MulSV(1000,b2Sub(material_point(s.positions,s.radius,(i+1)%16,.001f),next));CHECK(b2Distance(derivA,derivB)<.003f);
        for(int j=0;j<=10000;++j){b2Vec2 v=material_point(s.positions,s.radius,i,(float)j/10000);dense=fminf(dense,v.y);maxy=fmaxf(maxy,v.y);}}
    CHECK(fabsf(dense-p.point.y)<2e-7f && fabsf(maxy-p.maximum_y)<2e-7f);
    for(int shift=1;shift<16;++shift){b2Vec2 rotate[17];for(int i=0;i<16;++i)rotate[i]=s.positions[(i+shift)%16];rotate[16]=s.positions[16];dango_envelope_point r;CHECK(dango_envelope_geometry(rotate,s.radius,&r));CHECK(b2Distance(r.point,p.point)<2e-7f);}
    b2Vec2 sumN={0},sumT={0};float torqueN=0,torqueT=0;
    for(int i=0;i<17;++i){sumN=b2Add(sumN,p.normal[i]);sumT=b2Add(sumT,p.tangent[i]);torqueN+=b2Cross(s.positions[i],p.normal[i]);torqueT+=b2Cross(s.positions[i],p.tangent[i]);
        for(int axis=0;axis<2;++axis){b2Vec2 orig=s.positions[i];float h=.0005f;if(axis)s.positions[i].y+=h;else s.positions[i].x+=h;b2Vec2 plus=material_point(s.positions,s.radius,p.segment,p.t);s.positions[i]=orig;if(axis)s.positions[i].y-=h;else s.positions[i].x-=h;b2Vec2 minus=material_point(s.positions,s.radius,p.segment,p.t);s.positions[i]=orig;
            CHECK(fabsf((plus.y-minus.y)/(2*h)-(axis?p.normal[i].y:p.normal[i].x))<.0002f);CHECK(fabsf((plus.x-minus.x)/(2*h)-(axis?p.tangent[i].y:p.tangent[i].x))<.0002f);}}
    CHECK(b2Distance(sumN,(b2Vec2){0,1})<1e-6f && b2Distance(sumT,(b2Vec2){1,0})<1e-6f);CHECK(fabsf(torqueN-p.point.x)<1e-6f && fabsf(torqueT+p.point.y)<1e-6f);
    /* Normal finite difference of the re-minimized curve (envelope theorem). */
    for(int i=0;i<17;++i){float h=.0005f;s.positions[i].y+=h;dango_envelope_point plus;CHECK(dango_envelope_geometry(s.positions,s.radius,&plus));s.positions[i].y-=2*h;dango_envelope_point minus;CHECK(dango_envelope_geometry(s.positions,s.radius,&minus));s.positions[i].y+=h;CHECK(fabsf((plus.point.y-minus.point.y)/(2*h)-p.normal[i].y)<.0003f);}
    /* A deliberately non-convex two-valley fixture documents the nonunique argmin boundary.
       The scalar minimum remains equal; a deterministic first segment is not a globally continuous contact identity. */
    b2Vec2 ties[17],reindexed[17];for(int i=0;i<16;++i)ties[i]=(b2Vec2){(float)i-8,0};ties[16]=(b2Vec2){0,2};ties[4].y=ties[12].y=-2;
    dango_envelope_point tieA,tieB;CHECK(dango_envelope_geometry(ties,0,&tieA));for(int i=0;i<16;++i)reindexed[i]=ties[(i+8)%16];reindexed[16]=ties[16];CHECK(dango_envelope_geometry(reindexed,0,&tieB));CHECK(tieA.point.y==tieB.point.y && tieA.point.x!=tieB.point.x);
    /* Fixed material Jacobians on both representations of each C1 segment seam. */
    for(int seg=0;seg<16;++seg)for(int i=0;i<17;++i)for(int axis=0;axis<2;++axis){b2Vec2 original=s.positions[i];float h=.0005f;if(axis)s.positions[i].y+=h;else s.positions[i].x+=h;
        b2Vec2 aPlus=material_point(s.positions,s.radius,seg,1),bPlus=material_point(s.positions,s.radius,(seg+1)%16,0);s.positions[i]=original;if(axis)s.positions[i].y-=h;else s.positions[i].x-=h;
        b2Vec2 aMinus=material_point(s.positions,s.radius,seg,1),bMinus=material_point(s.positions,s.radius,(seg+1)%16,0);s.positions[i]=original;CHECK(b2Distance(b2Sub(aPlus,aMinus),b2Sub(bPlus,bMinus))<1e-7f);}
    puts("PASS analytic extrema / dense reference / seams / cyclic numbering / full Jacobian / momentum and torque");return 0;
}
static int impulse_contract(void){
    dango_constraint_state s=state();dango_envelope_point p;CHECK(dango_envelope_geometry(s.positions,s.radius,&p));float offset=s.ground-p.point.y-.0001f;for(int i=0;i<17;++i){s.positions[i].y+=offset;s.velocities[i]=(b2Vec2){2,-1};}
    float stats[32]={0};s.envelope_stats=stats;float energy=dango_envelope_energy(&s);b2Vec2 saved[17];memcpy(saved,s.positions,sizeof saved);
    for(int j=0;j<50;++j)CHECK(isfinite(dango_envelope_contact(&s,true,DANGO_CONSTRAINT_CORRECT_VELOCITY)));
    CHECK(!memcmp(saved,s.positions,sizeof saved));CHECK(stats[3]>0 && stats[4]>0 && stats[4]<=s.envelope_friction*stats[3]+1e-6f);CHECK(dango_envelope_energy(&s)<=energy+1e-6f);CHECK(stats[12]<=stats[11]+1e-6f && stats[14]<=stats[13]+1e-6f);CHECK(fabsf(stats[15])+fabsf(stats[16])+fabsf(stats[17])<2e-6f);
    b2Vec2 vel[17];memcpy(vel,s.velocities,sizeof vel);CHECK(isfinite(dango_envelope_contact(&s,true,DANGO_CONSTRAINT_CORRECT_POSITION)));CHECK(!memcmp(vel,s.velocities,sizeof vel));
    s.envelope_friction=0;s.envelope_normal=0;s.envelope_tangent_used=0;memset(stats,0,sizeof stats);for(int i=0;i<17;++i)s.velocities[i]=(b2Vec2){2,-1};CHECK(isfinite(dango_envelope_contact(&s,true,DANGO_CONSTRAINT_CORRECT_VELOCITY)));CHECK(stats[4]==0);
    for(int i=0;i<17;++i)s.positions[i].y+=2;memset(stats,0,sizeof stats);CHECK(isfinite(dango_envelope_contact(&s,true,DANGO_CONSTRAINT_CORRECT_BOTH)));CHECK(stats[3]==0 && stats[4]==0 && stats[6]==0);
    s=state();s.positions[0]=s.positions[16];CHECK(!dango_constraint_solve(&s));puts("PASS unilateral impulses / cumulative friction cone / dissipation / position velocity separation / no support / finite failure");return 0;
}
static int runworld(int direction){
    void* w=NULL;int id=0;CHECK(!dango_physics_world_create(&config,1,&w));CHECK(!dango_physics_world_add(w,0,2,&id));CHECK(!dango_physics_world_set_horizontal_bounds(w,-256,256));
    float c[9]={0},out[42],g[32];int sid,n,contacts,error;float peak=-INFINITY,min=INFINITY;double sumN=0,sumT=0;
    for(int frame=0;frame<1500;++frame){c[0]=frame>=300 && frame<900?(float)direction:0;c[2]=frame==1100?1.0f:0.0f;c[6]=frame>=950 && frame<1000?1.0f:0.0f;c[1]=frame>=1000 && frame<1050?1.0f:0.0f;
        c[3]=frame>=1200 && frame<1230?1.0f:0.0f;if(c[3]!=0){c[4]=out[32]+.2f;c[5]=out[33]+.2f;}
        int result=dango_physics_world_step_lab(w,1.0f/60,&id,c,1,0,0,&sid,out,42,&n,&contacts,&error);if(result || error){fprintf(stderr,"WORLD FAILURE direction %d frame %d result %d error %d\n",direction,frame,result,error);dango_physics_world_get_lab_ground_stats(w,id,g,32);for(int j=0;j<32;++j)fprintf(stderr,"g[%d]=%.9g\n",j,(double)g[j]);dango_physics_world_destroy(w);return 1;}
        CHECK(!dango_physics_world_get_lab_ground_stats(w,id,g,32));for(int j=0;j<32;++j)CHECK(isfinite(g[j]));CHECK(g[2]>=-1e-5f && g[20]<=1e-5f && g[27]==0 && g[30]==0);CHECK(g[3]>=0 && g[4]<=g[5]*g[3]+1e-6f);CHECK(g[12]<=g[11]+2e-6f && g[14]<=g[13]+2e-6f);CHECK(fabsf(g[15])+fabsf(g[16])+fabsf(g[17])<1e-4f);sumN+=g[3];sumT+=g[4];
        if(frame>=1100 && frame<1150)peak=fmaxf(peak,out[33]);if(frame>=1050 && frame<1100)min=fminf(min,out[33]);
    }
    printf("PASS real world direction=%d normal=%.9g tangent=%.9g jump core range=%.9g\n",direction,sumN,sumT,(double)(peak-min));CHECK(sumN>0);CHECK(peak-min>.05f);CHECK(!dango_physics_world_destroy(w));return 0;
}

static int airborne_and_fault_contract(void){
    HMODULE base=LoadLibraryA("dango_physics_lab.dll");CHECK(base!=NULL);
    int (*create)(const dango_self_physics_config*,int,void**)=NULL;int (*add)(void*,float,float,int*)=NULL;int(*destroy)(void*)=NULL;
    int(*step)(void*,float,const int*,const float*,int,int,float,int*,float*,int,int*,int*,int*)=NULL;
    FARPROC proc=GetProcAddress(base,"dango_physics_world_create");memcpy(&create,&proc,sizeof create);proc=GetProcAddress(base,"dango_physics_world_add");memcpy(&add,&proc,sizeof add);proc=GetProcAddress(base,"dango_physics_world_destroy");memcpy(&destroy,&proc,sizeof destroy);proc=GetProcAddress(base,"dango_physics_world_step_lab");memcpy(&step,&proc,sizeof step);CHECK(create && add && destroy && step);
    void* a=NULL;void* b=NULL;int ia=0,ib=0;CHECK(!create(&config,1,&a));CHECK(!dango_physics_world_create(&config,1,&b));CHECK(!add(a,0,20,&ia));CHECK(!dango_physics_world_add(b,0,20,&ib));
    float c[9]={0},sa[42],sb[42];int sid,n,contacts,error;
    for(int frame=0;frame<30;++frame){c[0]=frame>10?1.0f:0.0f;c[6]=frame>15?1.0f:0.0f;CHECK(!step(a,1.0f/60,&ia,c,1,0,0,&sid,sa,42,&n,&contacts,&error)&&!error);CHECK(!dango_physics_world_step_lab(b,1.0f/60,&ib,c,1,0,0,&sid,sb,42,&n,&contacts,&error)&&!error);CHECK(!memcmp(sa,sb,sizeof sa));}
    CHECK(!destroy(a));CHECK(!dango_physics_world_destroy(b));FreeLibrary(base);
    void* unsupported=NULL;
    for(int count=1;count<=3;++count) {unsupported=(void*)1;CHECK(dango_collision_create(&config,count,0,&unsupported)==-410);CHECK(unsupported==NULL);}
    CHECK(dango_physics_world_create(&config,2,&unsupported)==-410);CHECK(!dango_physics_world_create(&config,1,&b));CHECK(!dango_physics_world_add(b,0,2,&ib));CHECK(dango_physics_world_set_static_platform(b,0,1,4,.3f,.72f)==-410);
    dango_ball_definition ball={0};int ballId=0;CHECK(dango_physics_world_add_round_ball(b,&ball,&ballId)==-410);
    struct {unsigned long long world,revision,step;double gap;int rejection,reserved;} collectionProbe={0};
    unsigned long long token[4]={0};CHECK(!dango_physics_world_prepare_furniture_change(b,1,0,0,NULL,&collectionProbe,sizeof collectionProbe,token));CHECK(collectionProbe.rejection==2 && token[1]==0);
    struct {unsigned long long world,revision,step;float gap;int rejection;} platformProbe={0};
    CHECK(dango_physics_world_prepare_static_platform_change(b,1,0,0,NULL,&platformProbe,sizeof platformProbe,token)==-410);
    CHECK(!dango_physics_lab_test_collapse(b));memset(c,0,sizeof c);int first=dango_physics_world_step_lab(b,1.0f/60,&ib,c,1,0,0,&sid,sb,42,&n,&contacts,&error);CHECK(first || error);int saved=error;CHECK(saved!=0);CHECK(!dango_physics_world_step_lab(b,1.0f/60,&ib,c,1,0,0,&sid,sb,42,&n,&contacts,&error));CHECK(error==saved);CHECK(!dango_physics_world_destroy(b));
    puts("PASS airborne bitwise baseline equivalence / unsupported scenes rejected / real world shape fault latch");return 0;
}


typedef struct baseline_api {
    HMODULE module;
    int (*create)(const dango_self_physics_config*,int,void**);
    int (*add)(void*,float,float,int*);
    int (*bounds)(void*,float,float);
    int (*destroy)(void*);
    int (*step)(void*,float,const int*,const float*,int,int,float,int*,float*,int,int*,int*,int*);
} baseline_api;
static int load_baseline(const char* path,baseline_api* a){
    a->module=LoadLibraryA(path);CHECK(a->module);
#define LOAD(member,name) do{FARPROC proc=GetProcAddress(a->module,name);memcpy(&a->member,&proc,sizeof a->member);CHECK(a->member);}while(0)
    LOAD(create,"dango_physics_world_create");LOAD(add,"dango_physics_world_add");LOAD(bounds,"dango_physics_world_set_horizontal_bounds");LOAD(destroy,"dango_physics_world_destroy");LOAD(step,"dango_physics_world_step_lab");
#undef LOAD
    return 0;
}
static int rebuilt_baseline_contract(const char* oldPath){
    baseline_api a={0},b={0};CHECK(!load_baseline("dango_physics_lab.dll",&a));CHECK(!load_baseline(oldPath,&b));CHECK(a.module!=b.module);
    void* wa=NULL;void* wb=NULL;int ia,ib;CHECK(!a.create(&config,1,&wa)&&!b.create(&config,1,&wb));CHECK(!a.add(wa,0,2,&ia)&&!b.add(wb,0,2,&ib));CHECK(!a.bounds(wa,-256,256)&&!b.bounds(wb,-256,256));
    float c[9]={0},sa[42],sb[42];int sida,sidb,na,nb,ca,cb,ea,eb;
    for(int frame=0;frame<360;++frame){c[0]=frame>=60&&frame<150?1.0f:frame>=180&&frame<240?-1.0f:0.0f;c[1]=frame>=240&&frame<270?1.0f:0.0f;c[2]=frame==300?1.0f:0.0f;
        CHECK(!a.step(wa,1.0f/60,&ia,c,1,0,0,&sida,sa,42,&na,&ca,&ea));CHECK(!b.step(wb,1.0f/60,&ib,c,1,0,0,&sidb,sb,42,&nb,&cb,&eb));CHECK(!ea&&!eb&&na==nb&&ca==cb);CHECK(!memcmp(sa,sb,sizeof sa));
    }
    CHECK(!a.destroy(wa)&&!b.destroy(wb));FreeLibrary(a.module);FreeLibrary(b.module);puts("PASS rebuilt default lab vs protected historical lab: 360 ground frames default-v6 / +/-256 / left-right-S-Space bitwise");return 0;
}

int main(int argc,char** argv){CHECK(dango_physics_lab_ground_model()==1);CHECK(!geometry_contract());CHECK(!impulse_contract());CHECK(!airborne_and_fault_contract());CHECK(!runworld(0));CHECK(!runworld(1));CHECK(!runworld(-1));if(argc>1)CHECK(!rebuilt_baseline_contract(argv[1]));puts("PASS envelope native contracts");return 0;}

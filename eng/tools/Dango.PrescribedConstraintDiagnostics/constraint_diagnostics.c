/* R0 observers only. No solver change, private Box2D state, UI or network. */
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
struct dango_constraint_state;
#if DANGO_DIAGNOSTIC_VARIANT != 0
static void diagnostic_observe(const struct dango_constraint_state* state,int stage,int iteration);
static void diagnostic_context(void* world,int substep,int slot,int phase);
#define DANGO_CONSTRAINT_DIAGNOSTIC_OBSERVE diagnostic_observe
#define DANGO_PRESCRIBED_DIAGNOSTIC_CONTEXT diagnostic_context
#endif
#include DANGO_DIAGNOSTIC_BRIDGE_SOURCE
#include DANGO_DIAGNOSTIC_COLLISION_SOURCE

static const dango_self_physics_config frozen_config={
    .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
    .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=5.2f,.perimeter_hertz=20.1f,
    .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.ground_deformation_strength=3.5f,
    .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,.core_exclusion_ratio=.25f,
    .angle_strength=10,.local_angle_balance_strength=1.7f,.top_inward_force=.5f,.gravity=9.8f,.core_mass=.12f,.node_mass=.015f,
    .drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,.contact_hertz=28,
    .linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
    .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,
    .jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f
};
static uint64_t current_tick;
static DWORD owner_thread;
static int observer_mutations,io_errors,failed_calls,observed_calls,maximum_iterations;
static FILE *observations,*failure_details;

#if DANGO_DIAGNOSTIC_VARIANT != 0
typedef struct diagnostic_record {
    uint64_t tick,call;
    int32_t substep,entity,phase,stage,iteration,reserved;
    dango_constraint_state state;
} diagnostic_record;
static diagnostic_record call_records[98];
static int record_count,current_substep,current_entity,current_phase;
static void diagnostic_context(void* handle,int substep,int slot,int phase)
{
    collision_review* world=handle;
    if (substep) current_substep=substep;
    current_entity=world->entity_ids[slot]; current_phase=phase;
}
static void number(FILE* file,float value)
{
    if (isfinite(value)) fprintf(file,"%.9g",(double)value); else fputs("null",file);
}
static void detailed_record(const diagnostic_record* record)
{
    dango_constraint_state s=record->state;
    float residuals[16][5],raw[16][5],maximum=0; int worst_node=-1,worst_kind=-1;
    for (int n=0;n<16;++n) {
        float radial=b2Distance(s.positions[n],s.positions[16]);
        float perimeter=b2Distance(s.positions[n],s.positions[(n+1)%16]);
        raw[n][0]=fmaxf(s.radial_length*s.radial_min-radial,fmaxf(0,radial-s.radial_length*s.radial_max));
        raw[n][1]=fmaxf(s.perimeter_length*s.perimeter_min-perimeter,fmaxf(0,perimeter-s.perimeter_length*s.perimeter_max));
        residuals[n][0]=dango_constraint_distance(&s,16,n,s.radial_length*s.radial_min,s.radial_length*s.radial_max,false);
        residuals[n][1]=dango_constraint_distance(&s,n,(n+1)%16,s.perimeter_length*s.perimeter_min,s.perimeter_length*s.perimeter_max,false);
        raw[n][2]=residuals[n][2]=dango_constraint_angle(&s,n,false,false);
        raw[n][3]=residuals[n][3]=dango_constraint_angle(&s,n,true,false);
        raw[n][4]=fmaxf(0,s.ground+s.radius-s.positions[n].y);
        residuals[n][4]=raw[n][4]/s.radial_length;
        for (int kind=0;kind<5;++kind) if (residuals[n][kind]>maximum || !isfinite(residuals[n][kind])) {
            maximum=residuals[n][kind]; worst_node=n; worst_kind=kind;
        }
    }
    fprintf(failure_details,"{\"Tick\":%llu,\"Call\":%llu,\"Substep\":%d,\"Entity\":%d,\"Phase\":%d,\"Stage\":%d,\"Iteration\":%d,\"MaximumResidual\":",
        (unsigned long long)record->tick,(unsigned long long)record->call,record->substep,record->entity,record->phase,record->stage,record->iteration);
    number(failure_details,maximum);
    fprintf(failure_details,",\"Finite\":%s,\"WorstNode\":%d,\"WorstKind\":%d,\"Core\":[%.9g,%.9g],\"Rows\":[",
        isfinite(maximum) ? "true" : "false",worst_node,worst_kind,(double)s.positions[16].x,(double)s.positions[16].y);
    for (int n=0;n<16;++n) {
        if (n) fputc(',',failure_details);
        fprintf(failure_details,"{\"Node\":%d,\"Residuals\":[",n);
        for (int kind=0;kind<5;++kind) { if (kind) fputc(',',failure_details); number(failure_details,residuals[n][kind]); }
        fputs("],\"RawViolations\":[",failure_details);
        for (int kind=0;kind<5;++kind) { if (kind) fputc(',',failure_details); number(failure_details,raw[n][kind]); }
        fputs("]}",failure_details);
    }
    fputs("]}\n",failure_details);
}
static void diagnostic_observe(const dango_constraint_state* state,int stage,int iteration)
{
    dango_constraint_state before=*state;
    if (GetCurrentThreadId()!=owner_thread) ++observer_mutations;
    if (stage==0) { ++observed_calls; record_count=0; }
    if (record_count>=98) { ++io_errors; return; }
    diagnostic_record record={0}; record.tick=current_tick; record.call=(uint64_t)observed_calls;
    record.substep=current_substep; record.entity=current_entity; record.phase=current_phase;
    record.stage=stage; record.iteration=iteration; record.state=*state;
    call_records[record_count++]=record;
    if (fwrite(&record,sizeof record,1,observations)!=1) ++io_errors;
    if (stage==2 || stage==3) maximum_iterations=iteration>maximum_iterations ? iteration : maximum_iterations;
    if (stage==3) { ++failed_calls; for (int i=0;i<record_count;++i) detailed_record(&call_records[i]); }
#if DANGO_DIAGNOSTIC_VARIANT == 2
    if (current_tick==1 && stage==0) ((dango_constraint_state*)state)->positions[0].x+=.01f;
#endif
    if (memcmp(&before,state,sizeof before)!=0) ++observer_mutations;
}
#endif

typedef int (*create_fn)(const dango_self_physics_config*,int,void**);
typedef int (*add_fn)(void*,float,float,int*);
typedef int (*bind_fn)(void*,uint64_t);
typedef int (*step_fn)(void*,const dango_prescribed_step*,int,int*,float*,int,int*,int*,dango_prescribed_trace*,int);
typedef int (*destroy_fn)(void*);
typedef int (*bytes_fn)(void);
typedef struct frozen_api { create_fn create; add_fn add; bind_fn bind; step_fn step; destroy_fn destroy; bytes_fn bytes; } frozen_api;
static bool write_raw(FILE* output,collision_review* world)
{
    float raw[2][17][4]={0};
    for (int b=0;b<2;++b) for (int n=0;n<17;++n) {
        b2BodyId id=display_id(world->bodies[b],n); b2Vec2 p=b2Body_GetPosition(id),v=b2Body_GetLinearVelocity(id);
        raw[b][n][0]=p.x; raw[b][n][1]=p.y; raw[b][n][2]=v.x; raw[b][n][3]=v.y;
    }
    return fwrite(raw,sizeof raw,1,output)==1;
}
int main(int argc,char** argv)
{
    if (argc!=6) { fprintf(stderr,"fixture frozen-dll output-bin observations-bin failure-jsonl\n"); return 2; }
    owner_thread=GetCurrentThreadId(); int native_before=b2GetByteCount(),checks=0,failures=0;
    HMODULE library=LoadLibraryExA(argv[2],NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!library) return 2;
    frozen_api api={0};
#define LOAD(member,name,type) { FARPROC address=GetProcAddress(library,name); if (!address) { FreeLibrary(library); return 2; } \
    _Static_assert(sizeof address==sizeof api.member,"function pointer representation"); memcpy(&api.member,&address,sizeof address); }
    LOAD(create,"dango_physics_world_create",create_fn)
    LOAD(add,"dango_physics_world_add",add_fn)
    LOAD(bind,"dango_physics_world_bind_prescribed_epoch",bind_fn)
    LOAD(step,"dango_physics_world_step_prescribed",step_fn)
    LOAD(destroy,"dango_physics_world_destroy",destroy_fn)
    LOAD(bytes,"dango_physics_get_allocated_bytes",bytes_fn)
#undef LOAD
    int frozen_before=api.bytes(); FILE *input=NULL,*output=NULL;
    if (fopen_s(&input,argv[1],"rb") || fopen_s(&output,argv[3],"wb") ||
        fopen_s(&observations,argv[4],"wb") || fopen_s(&failure_details,argv[5],"w")) return 2;
    uint32_t header[4]={0};
    if (fread(header,sizeof header,1,input)!=1 || header[0]!=UINT32_C(0x30525044) || header[1]<1 || header[1]>600) return 2;
    void *handle=NULL,*reference=NULL; int ids[2]={0},reference_ids[2]={0};
    if (dango_physics_world_create(&frozen_config,2,&handle) || api.create(&frozen_config,2,&reference)) return 2;
    for (int b=0;b<2;++b) if (dango_physics_world_add(handle,b ? 10.0f : -10.0f,0,&ids[b]) ||
        api.add(reference,b ? 10.0f : -10.0f,0,&reference_ids[b]) || ids[b]!=reference_ids[b]) return 2;
    dango_prescribed_step first={0};
    if (fread(&first,sizeof first,1,input)!=1 || dango_physics_world_bind_prescribed_epoch(handle,first.epoch) || api.bind(reference,first.epoch)) return 2;
    int last_error=0,last_substeps=0,completed=0;
    float last_safe[84]={0}; bool have_safe=false,lastsafe_preserved=true;
    for (uint32_t tick=0;tick<header[1];++tick) {
        dango_prescribed_step requested=first;
        if (tick && fread(&requested,sizeof requested,1,input)!=1) { ++failures; break; }
        current_tick=requested.ordinal; int count=0,reference_count=0,contacts=0,reference_contacts=0;
        float states[84]={0},reference_states[84]={0}; int out_ids[2]={0},reference_out_ids[2]={0};
        dango_prescribed_trace trace={0},reference_trace={0};
        int result=dango_physics_world_step_prescribed(handle,&requested,248,out_ids,states,84,&count,&contacts,&trace,3936);
        int reference_result=api.step(reference,&requested,248,reference_out_ids,reference_states,84,&reference_count,&reference_contacts,&reference_trace,3936);
        ++checks;
        if (result!=reference_result || count!=reference_count || contacts!=reference_contacts ||
            memcmp(out_ids,reference_out_ids,sizeof out_ids) || memcmp(states,reference_states,sizeof states) ||
            memcmp(&trace,&reference_trace,sizeof trace)) ++failures;
        int step_header[5]={result,count,contacts,trace.error_code,trace.substep_count};
        if (fwrite(step_header,sizeof step_header,1,output)!=1 || fwrite(states,sizeof states,1,output)!=1 ||
            fwrite(&trace,sizeof trace,1,output)!=1 || !write_raw(output,handle)) ++io_errors;
        last_error=trace.error_code; last_substeps=trace.substep_count; completed=(int)tick+1;
        if (!last_error) { memcpy(last_safe,states,sizeof states); have_safe=true; }
        else {
            if (have_safe && memcmp(last_safe,states,sizeof states)) lastsafe_preserved=false;
            float preserved[84]; memcpy(preserved,states,sizeof preserved); int preserved_contacts=contacts;
            requested.ordinal++; requested.start_time=requested.end_time; requested.end_time++;
            dango_prescribed_trace next={0};
            if (dango_physics_world_step_prescribed(handle,&requested,248,out_ids,states,84,&count,&contacts,&next,3936) ||
                next.error_code!=last_error || memcmp(preserved,states,sizeof states) || contacts!=preserved_contacts) ++failures;
            break;
        }
    }
    if (completed!=(int)header[1] || last_error!=(int32_t)header[2] || last_substeps!=(int32_t)header[3] ||
        !lastsafe_preserved || fgetc(input)!=EOF || GetCurrentThreadId()!=owner_thread) ++failures;
    if (dango_physics_world_destroy(handle) || api.destroy(reference)) ++failures;
    int native_after=b2GetByteCount(),frozen_after=api.bytes();
    if (native_after!=native_before || frozen_after!=frozen_before) ++failures;
    if (fclose(input) || fclose(output) || fclose(observations) || fclose(failure_details)) ++io_errors;
    FreeLibrary(library);
    bool success=failures==0 && observer_mutations==0 && io_errors==0;
    printf("{\"Variant\":%d,\"ProcessId\":%lu,\"Success\":%s,\"ComparedSteps\":%d,\"Checks\":%d,\"ComparisonFailures\":%d,\"ObserverMutations\":%d,\"IoErrors\":%d,\"LastError\":%d,\"LastSubsteps\":%d,\"LastsafePreserved\":%s,\"ObservedCalls\":%d,\"FailedCalls\":%d,\"MaximumIterations\":%d,\"NativeBytesBefore\":%d,\"NativeBytesAfter\":%d,\"FrozenBytesBefore\":%d,\"FrozenBytesAfter\":%d,\"OwnerThread\":%lu}\n",
        DANGO_DIAGNOSTIC_VARIANT,GetCurrentProcessId(),success ? "true" : "false",completed,checks,failures,observer_mutations,io_errors,
        last_error,last_substeps,lastsafe_preserved ? "true" : "false",observed_calls,failed_calls,maximum_iterations,
        native_before,native_after,frozen_before,frozen_after,owner_thread);
    return success ? 0 : 1;
}

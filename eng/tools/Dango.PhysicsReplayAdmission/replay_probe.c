/* Isolated same-build replay executable. No production export, private Box2D
   structure, checkpoint import, network, account or persistence boundary. */
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#if defined(_WIN32)
#include <windows.h>
#else
#include <unistd.h>
#include <time.h>
#endif
#include DANGO_REPLAY_BRIDGE_SOURCE
#include DANGO_REPLAY_COLLISION_SOURCE

enum { REPLAY_VERSION=1, MAX_RECORDS=100000, FUTURE_STEPS=120, JOURNAL_BUDGET=65536 };
enum { ADD=1, REMOVE=2, BOUNDS=3, CLEAR_BOUNDS=4, RESET=5, STEP_SPAN=6 };
enum { FLOOR, COLLISION, STACK, AIRBORNE, REMOVED_LOWER, TOPOLOGY, STOP, LONG_HISTORY, ENTROPY, FIXTURE_COUNT };
static const char* fixture_names[FIXTURE_COUNT]={"floor","dynamic-collision","stack","airborne","removed-lower",
    "topology-boundaries-reset","stop","history-600s","entropy-600s"};
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

/* These are bounded, pointer-free test DTOs, not an upstream solver format.
   Their exact same-build representation is validated before reconstruction. */
typedef struct replay_header {
    char magic[8];
    uint32_t version,header_size,record_size,dt_bits,fixture,record_count,recovery_record_count,capture_pid;
    uint64_t recovery_tick;
    char build_id[72];
    dango_self_physics_config config;
    uint32_t reserved[2];
} replay_header;
typedef struct replay_record {
    uint32_t opcode,ordinal;
    uint64_t tick;
    uint32_t span,count;
    int32_t ids[2];
    float values[12];
    uint32_t reserved[2];
} replay_record;
typedef struct public_snapshot {
    uint32_t body_count,reserved;
    int32_t entity_ids[2];
    float dynamics[2][17][4]; /* raw x,y,vx,vy; never normalized contour */
    dango_authority_frame authority;
} public_snapshot;
typedef struct oracle_header {
    char magic[8];
    uint32_t version,snapshot_size,snapshot_count,capture_pid;
    char build_id[72];
} oracle_header;
typedef struct journal {
    replay_header header;
    replay_record* records;
    uint32_t count,allocated;
    bool split_next;
} journal;

static const uint32_t frozen_dt_bits=UINT32_C(0x3C888865);
static const char* failure_reason="none";
static double max_initial_position,max_initial_velocity,max_future_position,max_future_velocity;
static int semantic_mismatches;
static uint32_t future_compared;
static double restore_seconds,total_seconds;
static double future_seconds;
static uint64_t journal_bytes;
static bool correctness_passed,budget_passed,time_passed;
static uint32_t capture_pid;
static uint32_t owner_thread_id;
static bool owner_thread_violation;

static uint32_t current_pid(void) {
#if defined(_WIN32)
    return (uint32_t)GetCurrentProcessId();
#else
    return (uint32_t)getpid();
#endif
}
static double monotonic_seconds(void) {
#if defined(_WIN32)
    LARGE_INTEGER value,frequency;
    QueryPerformanceCounter(&value); QueryPerformanceFrequency(&frequency);
    return (double)value.QuadPart/(double)frequency.QuadPart;
#else
    struct timespec value; clock_gettime(CLOCK_MONOTONIC,&value);
    return (double)value.tv_sec+(double)value.tv_nsec/1e9;
#endif
}
static float actual_dt(void) { float value; memcpy(&value,&frozen_dt_bits,sizeof value); return value; }
static bool fail(const char* reason) { failure_reason=reason; return false; }
static uint32_t current_thread_id(void) {
#if defined(_WIN32)
    return (uint32_t)GetCurrentThreadId();
#else
    return current_pid(); /* Linux execution is not an admitted scope. */
#endif
}
static bool check_owner(void) {
    if (current_thread_id()==owner_thread_id) return true;
    owner_thread_violation=true; return fail("wrong-native-owner-thread");
}
static bool destroy_world(collision_review* world) {
    return check_owner() && (dango_physics_world_destroy(world)==0 || fail("world-destroy"));
}
static uint64_t hash_bytes(uint64_t hash,const void* bytes,size_t count) {
    const unsigned char* data=bytes;
    for (size_t index=0;index<count;++index) { hash^=data[index]; hash*=UINT64_C(1099511628211); }
    return hash;
}
static uint64_t journal_hash(const journal* log) {
    uint64_t value=hash_bytes(UINT64_C(14695981039346656037),&log->header,sizeof log->header);
    return hash_bytes(value,log->records,(size_t)log->count*sizeof *log->records);
}
static void new_journal(journal* log,int fixture) {
    memset(log,0,sizeof *log);
    memcpy(log->header.magic,"DGRPLY1",8);
    log->header.version=REPLAY_VERSION; log->header.header_size=sizeof(replay_header);
    log->header.record_size=sizeof(replay_record); log->header.dt_bits=frozen_dt_bits;
    log->header.fixture=(uint32_t)fixture; log->header.capture_pid=current_pid();
    memcpy(log->header.build_id,DANGO_REPLAY_BUILD_ID,65);
    log->header.config=frozen_config;
}
static bool append_record(journal* log,replay_record record) {
    if (record.opcode==STEP_SPAN && log->count && !log->split_next) {
        replay_record* last=&log->records[log->count-1];
        if (last->opcode==STEP_SPAN && last->tick+last->span==record.tick && last->count==record.count &&
            memcmp(last->ids,record.ids,sizeof record.ids)==0 && memcmp(last->values,record.values,sizeof record.values)==0) {
            ++last->span; return true;
        }
    }
    log->split_next=false;
    if (log->count>=MAX_RECORDS) return fail("record-limit");
    if (log->count==log->allocated) {
        uint32_t next=log->allocated ? log->allocated*2 : 128;
        if (next>MAX_RECORDS) next=MAX_RECORDS;
        replay_record* memory=realloc(log->records,(size_t)next*sizeof *memory);
        if (!memory) return fail("journal-allocation");
        log->records=memory; log->allocated=next;
    }
    record.ordinal=log->count+1; log->records[log->count++]=record;
    return true;
}
static uint64_t tick_of(collision_review* world) { return world->physics_step; }
static bool observe(collision_review* world,public_snapshot* snapshot) {
    if (!check_owner()) return false;
    memset(snapshot,0,sizeof *snapshot);
    if (dango_physics_world_get_authority_frame(world,&snapshot->authority,sizeof snapshot->authority)!=0 ||
        snapshot->authority.error_code!=0) return fail("unhealthy-authority");
    for (int slot=0;slot<world->capacity;++slot) if (world->active[slot]) {
        uint32_t body=snapshot->body_count++;
        if (body>=2) return fail("snapshot-count");
        snapshot->entity_ids[body]=world->entity_ids[slot];
        for (int point=0;point<17;++point) {
            /* Project-owned rig IDs locate bodies; only public Box2D getters read dynamics. */
            b2BodyId id=point==16 ? world->bodies[slot]->core_body_id : world->bodies[slot]->node_body_ids[point];
            b2Vec2 position=b2Body_GetPosition(id),velocity=b2Body_GetLinearVelocity(id);
            float* values=snapshot->dynamics[body][point];
            values[0]=position.x; values[1]=position.y; values[2]=velocity.x; values[3]=velocity.y;
            for (int axis=0;axis<4;++axis) if (!isfinite(values[axis])) return fail("nonfinite-dynamics");
        }
    }
    return snapshot->body_count==(uint32_t)snapshot->authority.body_count || fail("incoherent-body-count");
}
static collision_review* create_world(void) {
    if (!check_owner()) return NULL;
    void* handle=NULL;
    if (dango_physics_world_create(&frozen_config,2,&handle)!=0) { fail("world-create"); return NULL; }
    if (dango_physics_world_enable_authority(handle)!=0) { destroy_world(handle); fail("authority-enable"); return NULL; }
    return handle;
}
static bool step_world(collision_review* world,const replay_record* record) {
    if (!check_owner()) return false;
    int out_ids[3]={0},count=0,contacts=0,error=0;
    float states[126]={0};
    int result=dango_physics_world_step(world,actual_dt(),record->ids,record->values,(int)record->count,
        out_ids,states,126,&count,&contacts,&error);
    return (result==0 && error==0) || fail("step-failed");
}
static bool apply_nonstep(collision_review* world,const replay_record* record,bool capture,int* returned_id) {
    if (!check_owner()) return false;
    int result=-410,id=0;
    switch (record->opcode) {
        case ADD: result=dango_physics_world_add(world,record->values[0],record->values[1],&id); break;
        case REMOVE: result=dango_physics_world_remove(world,record->ids[0]); break;
        case BOUNDS: result=dango_physics_world_set_horizontal_bounds(world,record->values[0],record->values[1]); break;
        case CLEAR_BOUNDS: result=dango_physics_world_clear_horizontal_bounds(world); break;
        case RESET: result=dango_physics_world_reset(world); break;
        default: return fail("unknown-operation");
    }
    if (result!=0) return fail("operation-failed");
    if (record->opcode==ADD && !capture && id!=record->ids[0]) return fail("add-order-entity-mismatch");
    if (returned_id) *returned_id=id;
    return true;
}
static bool capture_operation(journal* log,collision_review* world,uint32_t opcode,int id,float x,float y) {
    replay_record record={0}; record.opcode=opcode; record.tick=tick_of(world);
    record.ids[0]=id; record.values[0]=x; record.values[1]=y;
    int returned=0;
    if (!apply_nonstep(world,&record,true,&returned)) return false;
    if (opcode==ADD) record.ids[0]=returned;
    return append_record(log,record);
}
static bool capture_step(journal* log,collision_review* world,const float controls[12]) {
    replay_record record={0}; record.opcode=STEP_SPAN; record.tick=tick_of(world); record.span=1;
    for (int slot=0;slot<world->capacity;++slot) if (world->active[slot]) {
        record.ids[record.count]=world->entity_ids[slot];
        memcpy(record.values+record.count*6,controls+slot*6,6*sizeof(float)); ++record.count;
    }
    if (!step_world(world,&record)) return false;
    return append_record(log,record);
}
static bool interference(void) {
    collision_review* world=create_world();
    if (!world) return false;
    int id=0; bool ok=check_owner() && (dango_physics_world_add(world,2.5f,0,&id)==0 || fail("interference-add"));
    replay_record input={0}; input.count=1; input.ids[0]=id;
    for (int step=0;ok && step<7;++step) ok=step_world(world,&input);
    if (!destroy_world(world)) ok=false;
    return ok;
}
static int history_steps(int fixture) {
    if (fixture==COLLISION || fixture==STOP) return 180;
    if (fixture==AIRBORNE) return 128;
    if (fixture==TOPOLOGY) return 250;
    if (fixture==LONG_HISTORY || fixture==ENTROPY) return 36000;
    return 120;
}
static void controls_for(int fixture,int step,bool future,float controls[12]) {
    memset(controls,0,12*sizeof(float));
    if (future) {
        if (fixture==FLOOR && step==40) controls[2]=1;
        if (fixture==STACK && (step==40 || step==42)) controls[8]=1;
        return;
    }
    if (fixture==COLLISION && step>=60) { controls[0]=1; controls[6]=-1; }
    if (fixture==AIRBORNE && step==120) controls[2]=1;
    if (fixture==TOPOLOGY && step>=40 && step<70) controls[0]=1;
    if (fixture==STOP && step>=120 && step<150) controls[0]=1;
    if (fixture==LONG_HISTORY) {
        int phase=(step/60)%8;
        controls[0]=phase==0 ? .25f : phase==4 ? -.25f : 0;
    }
    if (fixture==ENTROPY) {
        int phase=step/6;
        controls[0]=(float)(phase%17-8)/8; controls[6]=(float)((phase*3)%17-8)/8;
    }
}
static bool write_journal(const char* path,journal* log) {
    log->header.record_count=log->count;
    uint64_t hash=journal_hash(log);
    FILE* file=NULL;
    if (fopen_s(&file,path,"wb")!=0 || !file) return fail("journal-open-write");
    bool ok=fwrite(&log->header,sizeof log->header,1,file)==1 &&
        fwrite(log->records,sizeof *log->records,log->count,file)==log->count && fwrite(&hash,sizeof hash,1,file)==1;
    if (fclose(file)!=0) ok=false;
    journal_bytes=sizeof log->header+(uint64_t)log->count*sizeof *log->records+sizeof hash;
    return ok || fail("journal-write");
}
static bool capture(const char* fixture_name,const char* journal_path,const char* oracle_path) {
    int fixture=-1;
    for (int index=0;index<FIXTURE_COUNT;++index) if (strcmp(fixture_name,fixture_names[index])==0) fixture=index;
    if (fixture<0) return fail("fixture-name");
    journal log; new_journal(&log,fixture);
    collision_review* world=create_world(); FILE* oracle=NULL; bool ok=world!=NULL;
    if (ok && (fopen_s(&oracle,oracle_path,"wb")!=0 || !oracle)) ok=fail("oracle-open-write");
    oracle_header header={0}; memcpy(header.magic,"DGORCL1",8); header.version=REPLAY_VERSION;
    header.snapshot_size=sizeof(public_snapshot); header.snapshot_count=FUTURE_STEPS+1; header.capture_pid=current_pid();
    memcpy(header.build_id,DANGO_REPLAY_BUILD_ID,65);
    if (ok && fwrite(&header,sizeof header,1,oracle)!=1) ok=fail("oracle-header-write");
    bool pair=fixture==COLLISION || fixture==STACK || fixture==REMOVED_LOWER || fixture==TOPOLOGY || fixture==ENTROPY;
    bool stack=fixture==STACK || fixture==REMOVED_LOWER;
    if (ok) ok=capture_operation(&log,world,ADD,0,0,0);
    if (ok && pair) ok=capture_operation(&log,world,ADD,0,stack ? 0 : 2.8f,stack ? 1.65f : 0);
    if (ok && (fixture==TOPOLOGY || fixture==LONG_HISTORY || fixture==ENTROPY))
        ok=capture_operation(&log,world,BOUNDS,0,-13.2f,13.2f);
    int steps=history_steps(fixture);
    for (int step=0;ok && step<steps;++step) {
        if (fixture==TOPOLOGY) {
            if (step==30 || step==85) ok=interference();
            if (ok && step==100) ok=capture_operation(&log,world,REMOVE,2,0,0);
            if (ok && step==110) ok=capture_operation(&log,world,ADD,0,.5f,2);
            if (ok && step==160) ok=capture_operation(&log,world,CLEAR_BOUNDS,0,0,0);
            if (ok && step==170) ok=capture_operation(&log,world,BOUNDS,0,-13.2f,13.2f);
            if (ok && step==200) ok=capture_operation(&log,world,RESET,0,0,0);
        }
        float controls[12]; controls_for(fixture,step,false,controls);
        if (ok) ok=capture_step(&log,world,controls);
    }
    if (ok && fixture==REMOVED_LOWER) ok=capture_operation(&log,world,REMOVE,1,0,0);
    log.header.recovery_record_count=log.count; log.header.recovery_tick=world ? tick_of(world) : 0;
    log.split_next=true; public_snapshot snapshot;
    if (ok) ok=observe(world,&snapshot) && (fwrite(&snapshot,sizeof snapshot,1,oracle)==1 || fail("oracle-snapshot-write"));
    for (int step=0;ok && step<FUTURE_STEPS;++step) {
        float controls[12]; controls_for(fixture,step,true,controls);
        ok=capture_step(&log,world,controls) && observe(world,&snapshot) &&
            (fwrite(&snapshot,sizeof snapshot,1,oracle)==1 || fail("oracle-snapshot-write"));
    }
    if (ok) ok=write_journal(journal_path,&log);
    if (oracle && fclose(oracle)!=0) ok=fail("oracle-close");
    if (world && !destroy_world(world)) ok=false;
    free(log.records); budget_passed=journal_bytes<=JOURNAL_BUDGET; capture_pid=current_pid();
    return ok;
}
static bool read_journal(const char* path,journal* log) {
    memset(log,0,sizeof *log); FILE* file=NULL;
    if (fopen_s(&file,path,"rb")!=0 || !file) return fail("journal-open-read");
    bool ok=fseek(file,0,SEEK_END)==0; long length=ok ? ftell(file) : -1;
    if (length<0 || fseek(file,0,SEEK_SET)!=0) ok=fail("journal-length");
    if (ok && fread(&log->header,sizeof log->header,1,file)!=1) ok=fail("journal-short-header");
    replay_header* header=&log->header;
    if (ok && (memcmp(header->magic,"DGRPLY1",8)!=0 || header->version!=REPLAY_VERSION ||
        header->header_size!=sizeof *header || header->record_size!=sizeof(replay_record) ||
        header->dt_bits!=frozen_dt_bits || header->fixture>=FIXTURE_COUNT || !header->capture_pid || header->record_count==0 ||
        header->record_count>MAX_RECORDS || header->recovery_record_count==0 ||
        header->recovery_record_count>=header->record_count || header->reserved[0] || header->reserved[1] ||
        memcmp(header->build_id,DANGO_REPLAY_BUILD_ID,65)!=0 ||
        memcmp(&header->config,&frozen_config,sizeof frozen_config)!=0)) ok=fail("journal-version-config-build");
    if (ok && header->recovery_tick!=(uint64_t)history_steps((int)header->fixture)) ok=fail("journal-fixture-history");
    for (int index=65;ok && index<72;++index) if (header->build_id[index]) ok=fail("journal-build-padding");
    uint64_t expected=sizeof *header+(uint64_t)header->record_count*sizeof(replay_record)+sizeof(uint64_t);
    if (ok && (uint64_t)length!=expected) ok=fail("journal-exact-length");
    if (ok) {
        log->count=header->record_count; log->records=calloc(log->count,sizeof *log->records);
        if (!log->records) ok=fail("journal-allocation");
    }
    uint64_t expected_hash=0;
    if (ok && (fread(log->records,sizeof *log->records,log->count,file)!=log->count ||
        fread(&expected_hash,sizeof expected_hash,1,file)!=1 || expected_hash!=journal_hash(log))) ok=fail("journal-integrity");
    if (fclose(file)!=0) ok=fail("journal-close");
    journal_bytes=expected; capture_pid=header->capture_pid; budget_passed=journal_bytes<=JOURNAL_BUDGET;
    return ok;
}
static bool validate_records(const journal* log) {
    uint64_t tick=0; int active[2]={0},next_id=1;
    uint64_t final_tick=log->header.recovery_tick+FUTURE_STEPS;
    for (uint32_t index=0;index<log->count;++index) {
        const replay_record* record=&log->records[index];
        if (record->ordinal!=index+1 || record->tick!=tick || record->reserved[0] || record->reserved[1])
            return fail("record-ordinal-tick-segment");
        for (int axis=0;axis<12;++axis) if (!isfinite(record->values[axis])) return fail("record-nonfinite");
        if (record->opcode==STEP_SPAN) {
            if (record->span<1 || record->span>36000 || record->count>2 || tick>final_tick ||
                record->span>final_tick-tick) return fail("record-span-count");
            int count=0;
            for (int slot=0;slot<2;++slot) if (active[slot]) {
                if ((uint32_t)count>=record->count || record->ids[count]!=active[slot]) return fail("record-step-identity");
                const float* input=record->values+count*6;
                if (input[0]<-1 || input[0]>1 || input[1]<0 || input[1]>1 ||
                    (input[2]!=0 && input[2]!=1) || (input[3]!=0 && input[3]!=1) ||
                    (input[2]!=0 && record->span!=1)) return fail("record-controls-edge");
                ++count;
            }
            if ((uint32_t)count!=record->count) return fail("record-missing-entity");
            for (uint32_t slot=record->count;slot<2;++slot) {
                if (record->ids[slot]) return fail("record-unused-fields");
                for (int axis=0;axis<6;++axis) if (record->values[slot*6+axis]!=0) return fail("record-unused-fields");
            }
            if (index<log->header.recovery_record_count && tick+record->span>log->header.recovery_tick)
                return fail("record-recovery-boundary");
            tick+=record->span;
        } else {
            if (record->span || record->count) return fail("record-nonstep-fields");
            if (record->ids[1]) return fail("record-unused-fields");
            int used_values=(record->opcode==ADD || record->opcode==BOUNDS) ? 2 : 0;
            for (int axis=used_values;axis<12;++axis) if (record->values[axis]!=0) return fail("record-unused-fields");
            if (record->opcode==ADD) {
                int slot=active[0]==0 ? 0 : active[1]==0 ? 1 : -1;
                if (slot<0 || record->ids[0]!=next_id++) return fail("record-add-order");
                active[slot]=record->ids[0];
            } else if (record->opcode==REMOVE) {
                int slot=active[0]==record->ids[0] ? 0 : active[1]==record->ids[0] ? 1 : -1;
                if (slot<0 || record->ids[0]<=0) return fail("record-remove-identity");
                active[slot]=0;
            } else {
                if (record->ids[0]) return fail("record-unused-fields");
                if (record->opcode==BOUNDS) {
                    float left=record->values[0],right=record->values[1];
                    if (fabsf(left)>1024 || fabsf(right)>1024 || !isfinite(right-left) ||
                        right-left<2*(frozen_config.radial_rest_length+frozen_config.node_radius)) return fail("record-bounds");
                } else if (record->opcode!=CLEAR_BOUNDS && record->opcode!=RESET) return fail("record-opcode");
            }
        }
        if (index+1==log->header.recovery_record_count && tick!=log->header.recovery_tick)
            return fail("record-recovery-boundary");
    }
    return tick==final_tick || fail("record-future-length");
}
static bool compare_snapshot(const public_snapshot* actual,const public_snapshot* expected,bool initial) {
    if (expected->reserved || expected->body_count>2 || actual->body_count!=expected->body_count ||
        memcmp(actual->entity_ids,expected->entity_ids,sizeof actual->entity_ids)!=0)
        return fail("oracle-identity");
    double position=0,velocity=0;
    for (uint32_t body=0;body<actual->body_count;++body) for (int point=0;point<17;++point) for (int axis=0;axis<4;++axis) {
        double delta=fabs((double)actual->dynamics[body][point][axis]-expected->dynamics[body][point][axis]);
        if (!isfinite(expected->dynamics[body][point][axis])) return fail("oracle-nonfinite");
        if (axis<2) position=fmax(position,delta); else velocity=fmax(velocity,delta);
    }
    if (initial) { max_initial_position=fmax(max_initial_position,position); max_initial_velocity=fmax(max_initial_velocity,velocity); }
    else { max_future_position=fmax(max_future_position,position); max_future_velocity=fmax(max_future_velocity,velocity); ++future_compared; }
    bool semantics=memcmp(&actual->authority,&expected->authority,sizeof actual->authority)==0;
    if (!semantics) ++semantic_mismatches;
    if (!semantics || position>(initial ? 1e-6 : 1e-5) || velocity>(initial ? 1e-6 : 1e-4))
        return fail(initial ? "oracle-initial-dynamics-semantics" : "oracle-future-dynamics-semantics");
    return true;
}
static bool compare_from_file(collision_review* world,FILE* oracle,bool initial) {
    public_snapshot actual,expected;
    if (fread(&expected,sizeof expected,1,oracle)!=1) return fail("oracle-short-snapshot");
    return observe(world,&actual) && compare_snapshot(&actual,&expected,initial);
}
static bool replay(const char* journal_path,const char* oracle_path,bool diagnostic) {
    double started=monotonic_seconds(); journal log; bool ok=read_journal(journal_path,&log);
    FILE* oracle=NULL; collision_review* world=NULL;
    if (ok) ok=validate_records(&log);
    if (ok && capture_pid==current_pid()) ok=fail("same-process-capture-replay");
    if (ok && !budget_passed && !diagnostic) ok=fail("journal-budget");
    if (ok && (fopen_s(&oracle,oracle_path,"rb")!=0 || !oracle)) ok=fail("oracle-open-read");
    oracle_header header={0};
    if (ok && (fread(&header,sizeof header,1,oracle)!=1 || memcmp(header.magic,"DGORCL1",8)!=0 ||
        header.version!=REPLAY_VERSION || header.snapshot_size!=sizeof(public_snapshot) ||
        header.snapshot_count!=FUTURE_STEPS+1 || header.capture_pid!=capture_pid ||
        memcmp(header.build_id,DANGO_REPLAY_BUILD_ID,65)!=0)) ok=fail("oracle-version-build");
    if (ok && log.header.fixture==TOPOLOGY) ok=interference() && interference();
    if (ok) { world=create_world(); ok=world!=NULL; }
    double future_started=0;
    for (uint32_t index=0;ok && index<log.count;++index) {
        const replay_record* record=&log.records[index];
        if (record->tick!=tick_of(world)) { ok=fail("runtime-tick"); break; }
        bool future=index>=log.header.recovery_record_count;
        if (record->opcode==STEP_SPAN) {
            for (uint32_t step=0;ok && step<record->span;++step) {
                ok=step_world(world,record);
                if (ok && future) ok=compare_from_file(world,oracle,false);
            }
        } else ok=apply_nonstep(world,record,false,NULL);
        if (ok && index+1==log.header.recovery_record_count) {
            public_snapshot safe;
            ok=observe(world,&safe);
            restore_seconds=monotonic_seconds()-started;
            time_passed=restore_seconds<=5;
            if (ok) ok=compare_from_file(world,oracle,true);
            future_started=monotonic_seconds();
        }
    }
    if (future_started>0) future_seconds=monotonic_seconds()-future_started;
    if (ok && (future_compared!=FUTURE_STEPS || fgetc(oracle)!=EOF)) ok=fail("oracle-exact-future-length");
    if (oracle && fclose(oracle)!=0) ok=fail("oracle-close");
    if (world && !destroy_world(world)) ok=false;
    free(log.records); total_seconds=monotonic_seconds()-started;
    correctness_passed=ok;
    if (ok && !budget_passed) return fail("journal-budget-diagnostic");
    if (ok && !time_passed) return fail("restore-time-budget");
    return ok;
}
static bool mutate(const char* input_path,const char* output_path,const char* variant) {
    journal log; bool ok=read_journal(input_path,&log),changed=false;
    if (!ok) { free(log.records); return false; }
    if (strcmp(variant,"missing-jump")==0) {
        for (uint32_t index=0;index<log.header.recovery_record_count && !changed;++index) {
            replay_record* record=&log.records[index];
            if (record->opcode!=STEP_SPAN) continue;
            for (uint32_t body=0;body<record->count;++body) if (record->values[body*6+2]!=0) {
                record->values[body*6+2]=0; changed=true; break;
            }
        }
    } else if (strcmp(variant,"lost-stop")==0) {
        float held=0;
        for (uint32_t index=0;index<log.header.recovery_record_count && !changed;++index) {
            replay_record* record=&log.records[index]; if (record->opcode!=STEP_SPAN || !record->count) continue;
            if (held!=0 && record->values[0]==0) { record->values[0]=held; changed=true; }
            held=record->values[0];
        }
    } else if (strcmp(variant,"add-order")==0) {
        int first=-1;
        for (uint32_t index=0;index<log.count && !changed;++index) if (log.records[index].opcode==ADD) {
            if (first<0) first=(int)index;
            else {
                replay_record a=log.records[first],b=log.records[index];
                b.ordinal=a.ordinal; a.ordinal=index+1; log.records[first]=b; log.records[index]=a; changed=true;
            }
        }
    } else if (strcmp(variant,"tick")==0) { ++log.records[log.count-1].tick; changed=true; }
    else if (strcmp(variant,"segment")==0) {
        for (uint32_t index=0;index<log.count && !changed;++index) if (log.records[index].opcode==STEP_SPAN) {
            memmove(log.records+index,log.records+index+1,(size_t)(log.count-index-1)*sizeof *log.records);
            --log.count; --log.header.recovery_record_count;
            for (uint32_t ordinal=0;ordinal<log.count;++ordinal) log.records[ordinal].ordinal=ordinal+1;
            changed=true;
        }
    } else if (strcmp(variant,"version")==0) { ++log.header.version; changed=true; }
    else if (strcmp(variant,"build-id")==0) { log.header.build_id[0]=log.header.build_id[0]=='0' ? '1' : '0'; changed=true; }
    else if (strcmp(variant,"dt")==0) { ++log.header.dt_bits; changed=true; }
    else if (strcmp(variant,"length")==0) { ++log.header.record_size; changed=true; }
    if (!changed) ok=fail("mutation-not-exercised");
    /* Recompute integrity: Jump/Stop mutations must reach the dynamic oracle. */
    if (ok) ok=write_journal(output_path,&log);
    free(log.records); return ok;
}
int main(int argc,char** argv) {
    owner_thread_id=current_thread_id();
    int initial_bytes=b2GetByteCount(); double started=monotonic_seconds(); bool ok=false;
    const char* mode=argc>1 ? argv[1] : "usage";
    if (strcmp(mode,"capture")==0 && argc==5) ok=capture(argv[2],argv[3],argv[4]);
    else if (strcmp(mode,"replay")==0 && (argc==4 || argc==5))
        ok=replay(argv[2],argv[3],argc==5 && strcmp(argv[4],"--oversize-diagnostic")==0);
    else if (strcmp(mode,"mutate")==0 && argc==5) ok=mutate(argv[2],argv[3],argv[4]);
    else { failure_reason="usage"; fprintf(stderr,"capture fixture journal oracle | replay journal oracle [--oversize-diagnostic] | mutate in out variant\n"); }
    int after_bytes=b2GetByteCount(); bool released=after_bytes==initial_bytes;
    if (!released) { ok=false; failure_reason="native-allocation-not-returned"; }
    printf("{\"Suite\":\"same-build-replay\",\"Mode\":\"%s\",\"Success\":%s,\"FailureReason\":\"%s\","
        "\"ProcessId\":%u,\"CaptureProcessId\":%u,\"DifferentProcess\":%s,\"BuildId\":\"%s\","
        "\"DtBits\":\"0x3C888865\",\"ActualAdaptiveSubsteps\":%d,\"JournalBytes\":%llu,\"JournalBudgetPassed\":%s,"
        "\"CorrectnessPassed\":%s,\"RestoreSeconds\":%.9g,\"RestoreTimeBudgetPassed\":%s,\"FutureCompareSeconds\":%.9g,\"TotalSeconds\":%.9g,"
        "\"InitialPositionError\":%.9g,\"InitialVelocityError\":%.9g,\"FuturePositionError\":%.9g,\"FutureVelocityError\":%.9g,"
        "\"FutureComparedSteps\":%u,\"SemanticMismatches\":%d,\"NativeBytesBefore\":%d,\"NativeBytesAfter\":%d,"
        "\"ResourcesReturned\":%s,\"OwnerThreadId\":%u,\"OwnerThreadViolation\":%s,\"FullCheckpointSupported\":false,\"Real600SecondObservation\":false,\"LinuxVerified\":false}\n",
        mode,ok ? "true" : "false",failure_reason,current_pid(),capture_pid,
        capture_pid && capture_pid!=current_pid() ? "true" : "false",DANGO_REPLAY_BUILD_ID,
        (int)ceilf(actual_dt()*960.0f),(unsigned long long)journal_bytes,budget_passed ? "true" : "false",
        correctness_passed ? "true" : "false",restore_seconds,time_passed ? "true" : "false",future_seconds,
        total_seconds>0 ? total_seconds : monotonic_seconds()-started,max_initial_position,max_initial_velocity,
        max_future_position,max_future_velocity,future_compared,semantic_mismatches,initial_bytes,after_bytes,released ? "true" : "false",
        owner_thread_id,owner_thread_violation ? "true" : "false");
    return ok ? 0 : 1;
}

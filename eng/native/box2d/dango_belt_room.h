#ifndef DANGO_BELT_ROOM_H
#define DANGO_BELT_ROOM_H
#include "dango_belt.h"
#include <stdint.h>
#if defined(_WIN32)
#define DANGO_BELT_ROOM_API __declspec(dllexport)
#else
#define DANGO_BELT_ROOM_API __attribute__((visibility("default")))
#endif
/* Independent room ABI. Handles must never cross the single-body API. */
enum { DANGO_BELT_ROOM_ABI=1, DANGO_BELT_ROOM_BODY_CAPACITY=1,
 DANGO_BELT_ROOM_PLATFORM_CAPACITY=1, DANGO_BELT_ROOM_DIAGNOSTICS=24 };
enum { DANGO_BELT_ROOM_CAP_SNAPSHOT=1,DANGO_BELT_ROOM_CAP_FROZEN_BODY=2,
 DANGO_BELT_ROOM_CAP_SOLID_PLATFORM=4,DANGO_BELT_ROOM_CAP_ATOMIC_LAYOUT=8,
 DANGO_BELT_ROOM_CAP_HORIZONTAL_BOUNDS=16,DANGO_BELT_ROOM_CAP_SOLID_WALLS=32 };
enum { DANGO_BELT_ROOM_ARGUMENT=-700,DANGO_BELT_ROOM_HANDLE=-701,
 DANGO_BELT_ROOM_CONFIG=-702,DANGO_BELT_ROOM_FAULT=-703,
 DANGO_BELT_ROOM_CAPACITY=-704,DANGO_BELT_ROOM_REJECTED=-705,
 DANGO_BELT_ROOM_BUDGET=-706,DANGO_BELT_ROOM_UNCERTAIN=-707,
 DANGO_BELT_ROOM_REVISION=-708 };
typedef struct dango_belt_room_input {
 uint64_t body_id;float controls[9];int32_t reserved;
} dango_belt_room_input;
typedef struct dango_belt_room_platform {
 uint64_t id;double x,y,width,height,friction;
} dango_belt_room_platform;
typedef struct dango_belt_room_frame {
 uint32_t size,abi;
 uint64_t generation,tick,scene_revision,body_id,config_revision;
 int32_t platform_enabled,fault;
 dango_belt_room_platform platform;
 double body_snapshot[200];
 float scaffold[68],curve[512],stats[64];
 double diagnostics[DANGO_BELT_ROOM_DIAGNOSTICS];
} dango_belt_room_frame;
DANGO_BELT_ROOM_API int dango_belt_room_version(void);
DANGO_BELT_ROOM_API int dango_belt_room_capabilities(void);
DANGO_BELT_ROOM_API int dango_belt_room_frame_size(void);
DANGO_BELT_ROOM_API const char* dango_belt_room_model(void);
DANGO_BELT_ROOM_API const char* dango_belt_room_tuning(void);
DANGO_BELT_ROOM_API const char* dango_belt_room_contact_model(void);
DANGO_BELT_ROOM_API int dango_belt_room_create(const dango_belt_config*,const float*,int,
 float,float,float,float,void**);
DANGO_BELT_ROOM_API int dango_belt_room_step(void*,float,const dango_belt_room_input*,int);
/* Optional single-step internal shake input: time [0,2], strength [0,2].
   No held input; zero strength is exactly the ordinary path. */
DANGO_BELT_ROOM_API int dango_belt_room_step_muscles(void*,float,const dango_belt_room_input*,int,float,float);
/* Optional profile: 0 preserves the original fibres, 1 uses soft shoulders. */
DANGO_BELT_ROOM_API int dango_belt_room_step_muscles_profile(void*,float,const dango_belt_room_input*,int,float,float,int);
/* Optional profile2: current eye-line world unit axis plus replayable seed. */
DANGO_BELT_ROOM_API int dango_belt_room_step_muscles_pattern(void*,float,const dango_belt_room_input*,int,float,float,int,float,float,uint32_t);
/* v1, 32 doubles: 0 version,1 count,2 attempt,3 time,4 strength,5 active substeps,
   6 pairs,7/8 net force,9 peak abs torque,10 actuator work,11 peak tension,
   12 minimum contact weight,13 envelope,14 total follower iterations,
   15 maximum follower residual,16..23 last fibre tensions,24 active profile. */
DANGO_BELT_ROOM_API int dango_belt_room_read_muscle_diagnostics(void*,double*,int);
/* Detached last successful substep's active point-force ledger (1024 doubles).
   Excludes gravity, passive elastic/contact/constraint forces and jump impulses. */
DANGO_BELT_ROOM_API int dango_belt_room_read_active_forces(void*,double*,int);
DANGO_BELT_ROOM_API int dango_belt_room_read_frame(void*,dango_belt_room_frame*,int);
/* Changes the interval; solid contact is enabled separately. Shares layout revision. */
DANGO_BELT_ROOM_API int dango_belt_room_set_bounds(void*,float,float,uint64_t);
/* Optional solid half-space contact at current bounds; default is guard-only. */
DANGO_BELT_ROOM_API int dango_belt_room_set_solid_walls(void*,int,uint64_t);
/* Optional isolated-preview actuation: per world, finite [0.1,1], default 1.
   Does not change layout revisions or frame/step ABI. */
DANGO_BELT_ROOM_API int dango_belt_room_set_preview_jump_speed_scale(void*,float);
DANGO_BELT_ROOM_API int dango_belt_room_set_platform(void*,const dango_belt_room_platform*,uint64_t);
DANGO_BELT_ROOM_API int dango_belt_room_remove_platform(void*,uint64_t,uint64_t);
DANGO_BELT_ROOM_API int dango_belt_room_destroy(void*);
#endif

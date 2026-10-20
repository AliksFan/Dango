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
 DANGO_BELT_ROOM_CAP_SOLID_PLATFORM=4,DANGO_BELT_ROOM_CAP_ATOMIC_LAYOUT=8 };
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
DANGO_BELT_ROOM_API int dango_belt_room_read_frame(void*,dango_belt_room_frame*,int);
DANGO_BELT_ROOM_API int dango_belt_room_set_platform(void*,const dango_belt_room_platform*,uint64_t);
DANGO_BELT_ROOM_API int dango_belt_room_remove_platform(void*,uint64_t,uint64_t);
DANGO_BELT_ROOM_API int dango_belt_room_destroy(void*);
#endif

#ifndef DANGO_BELT_ROOM_COLLECTION_H
#define DANGO_BELT_ROOM_COLLECTION_H
#include "dango_belt_room.h"
#include "dango_round_ball_types.h"
enum { DANGO_BELT_ROOM_COLLECTION_ABI=1,DANGO_BELT_ROOM_COLLECTION_MAX=30,
 DANGO_BELT_ROOM_CAP_COLLECTION=64,DANGO_BELT_ROOM_CAP_BALLS=128,DANGO_BELT_ROOM_BUSY=-709 };
/* All coordinates, radii, velocities and contact data use room meters.
   The legacy p/q transform does not apply to this independent extension. */
typedef struct dango_belt_room_definition {
 int32_t kind,reserved;
 double x,y,width,height,friction;
 dango_ball_definition ball;
} dango_belt_room_definition;
typedef struct dango_belt_room_collection_item {
 uint64_t instance;
 dango_belt_room_definition definition;
 dango_ball_state ball;
 dango_ball_contact body_contact,surface_contact;
} dango_belt_room_collection_item;
typedef struct dango_belt_room_collection_frame {
 dango_belt_room_frame base;
 uint64_t last_applied_token;
 int32_t item_count,enabled;
 dango_belt_room_collection_item items[DANGO_BELT_ROOM_COLLECTION_MAX];
} dango_belt_room_collection_frame;
typedef struct dango_belt_room_collection_probe {
 uint64_t generation,scene_revision,tick,config_revision;
 double minimum_gap;
 int32_t rejection,reserved;
} dango_belt_room_collection_probe;
typedef struct dango_belt_room_collection_token {
 uint64_t generation,token_id,scene_revision,tick,config_revision;
} dango_belt_room_collection_token;
DANGO_BELT_ROOM_API int dango_belt_room_collection_version(void);
DANGO_BELT_ROOM_API int dango_belt_room_collection_frame_size(void);
DANGO_BELT_ROOM_API const char* dango_belt_room_collection_model(void);
DANGO_BELT_ROOM_API int dango_belt_room_enable_collection(void*,uint64_t);
DANGO_BELT_ROOM_API int dango_belt_room_read_collection_frame(void*,dango_belt_room_collection_frame*,int);
/* kind: 1 add, 2 move, 3 remove. Rejection is returned in probe; API failures
   return a negative room error. Add allocates an immutable uint64 identity. */
DANGO_BELT_ROOM_API int dango_belt_room_probe_furniture_change(void*,int,uint64_t,uint64_t,
 const dango_belt_room_definition*,dango_belt_room_collection_probe*,int);
DANGO_BELT_ROOM_API int dango_belt_room_prepare_furniture_change(void*,int,uint64_t,uint64_t,
 const dango_belt_room_definition*,dango_belt_room_collection_probe*,int,dango_belt_room_collection_token*);
DANGO_BELT_ROOM_API int dango_belt_room_apply_prepared_furniture_change(void*,const dango_belt_room_collection_token*);
DANGO_BELT_ROOM_API int dango_belt_room_cancel_prepared_furniture_change(void*,const dango_belt_room_collection_token*);
#endif

#include "dango_belt_room_collection.h"
#ifndef DANGO_BELT_ROOM_COLLECTION_IDENTITY_VARIANT
#define DANGO_BELT_ROOM_COLLECTION_IDENTITY_VARIANT 0
#endif
int dango_belt_room_version(void){return 1;}
int dango_belt_room_capabilities(void){return DANGO_BELT_ROOM_COLLECTION_IDENTITY_VARIANT==4?79:207;}
int dango_belt_room_frame_size(void){return 4472;}
const char* dango_belt_room_model(void){return "continuous-belt-room-v1";}
const char* dango_belt_room_tuning(void){return "C455-F-v1";}
const char* dango_belt_room_contact_model(void){return "belt-solid-aabb-r1-v1";}
int dango_belt_room_collection_version(void){return DANGO_BELT_ROOM_COLLECTION_IDENTITY_VARIANT==1?2:1;}
int dango_belt_room_collection_frame_size(void){return DANGO_BELT_ROOM_COLLECTION_IDENTITY_VARIANT==2?16960:16968;}
const char* dango_belt_room_collection_model(void){return DANGO_BELT_ROOM_COLLECTION_IDENTITY_VARIANT==3?"wrong-model":"belt-room-furniture-r3-v1";}

#ifndef DANGO_PHYSICS_LAB_API_H
#define DANGO_PHYSICS_LAB_API_H
#include "dango_physics_module.h"
/* Optional, isolated DLL capability. Neither export exists in default modules. */
DANGO_API int dango_physics_lab_version(void);
DANGO_API int dango_physics_get_lab_version(void);
/* Each control is nine floats: horizontal [-1,1], downward [0,1], jump bit,
   drag bit, target X/Y, stretch [0,1], actual input-active bit, cancel-forces bit.
   Input-active is diagnostic occupancy, not a ramp-enable switch. Cancel clears
   the ramp immediately; normal stretch=0 release uses .20s full-scale decay.
   Accepted jump frames keep ramp unchanged and omit W and S forces for that
   whole fixed frame. Rejected jump requests retain ordinary W/S behavior.
   Mode 0 core, 1 actual-mass distribution, 2 height-weighted distribution.
   Mode 3 interpolates the force between the nearest actual body centers below/
   above coreY+bias*(positive: maxY-coreY; negative: coreY-minY).
   Bias [-1,1], positive toward world-up. Mode 2 is a distribution weight;
   mode 3 is the actual resultant height. Mode 3 bias zero uses the old core path. */
DANGO_API int dango_physics_world_step_lab(void* handle,float dt,const int* entity_ids,
    const float* controls9,int count,int mode,float bias,int* state_ids,float* states,
    int state_capacity,int* out_count,int* out_contacts,int* out_error);
/* Last small-step stats, 16 floats:
   0 mode, 1 bias, 2 stretch ramp, 3 requested Fx after original speed gate,
   4 actual Fx, 5 actual Fy, 6 drive torque about actual COM,
   7 force-weighted actual world Y (idle: core Y for mode 0/zero-bias 3,
     target Y for mode 3, COM Y for other modes), 8 actual COM Y,
   9 sum of node-side stretch force magnitudes, 10/11 stretch net Fx/Fy,
   12 stretch torque about core, 13 configured downward-force budget,
   14 actual input-active, 15 requested stretch.
   This measures controller force only, not contact/gravity/internal reactions.
   Read-only, -410 invalid args, -412 missing entity. */
DANGO_API int dango_physics_world_get_lab_stats(void* handle,int entity_id,float* stats,int capacity);
/* Existing D1 layout: core + sixteen nodes, each x,y,vx,vy,mass,contact count. */
DANGO_API int dango_physics_world_get_drive_review(void* handle,int entity_id,float* samples,int capacity);
/* Optional DANGO_LAB_REGIONS capability only. Modes 4/5 use physical contour
   nodes in the left/right/top outer third of the exact rendered spline AABB.
   4 shares one force by actual mass; 5 uses its center-nearest in-region node.
   S cancels W release immediately; accepted Space freezes/omits both as above.
   Stats: minX,minY,maxX,maxY, horizontal/vertical node masks (bits 0..15),
   horizontal point XY, vertical point XY, actual resultant XY, torque about
   actual COM, reserved[2], schema=1. Last physical small step only. */
DANGO_API int dango_physics_lab_region_version(void);
DANGO_API int dango_physics_world_get_lab_region_stats(void* handle,int entity_id,float* stats,int capacity);
#endif

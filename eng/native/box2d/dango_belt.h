#ifndef DANGO_BELT_H
#define DANGO_BELT_H
#if defined(_WIN32)
#define DANGO_BELT_API __declspec(dllexport)
#else
#define DANGO_BELT_API __attribute__((visibility("default")))
#endif
/* Deliberately identical scalar layout to the old self config, without Box2D. */
typedef struct dango_belt_config {
 int node_count,substep_count;
 float radial_rest_length,radial_minimum_ratio,radial_maximum_ratio;
 float perimeter_minimum_ratio,perimeter_maximum_ratio;
 float radial_hertz,perimeter_hertz,radial_damping,perimeter_damping;
 float area_strength,ground_deformation_strength,repulsion_distance,repulsion_strength;
 float repulsion_maximum_force,core_exclusion_ratio,angle_strength,local_angle_balance_strength;
 float top_inward_force,gravity,core_mass,node_mass,drop_height,ground_height,node_radius;
 float friction,restitution,contact_hertz,linear_damping,drag_stiffness,drag_damping;
 float drag_maximum_force,horizontal_force,maximum_horizontal_speed,air_control_ratio;
 float downward_force,jump_impulse,jump_cooldown_seconds,maximum_speed,minimum_area;
} dango_belt_config;
enum { DANGO_BELT_SCAFFOLD_FLOATS=68,DANGO_BELT_CURVE_FLOATS=512,DANGO_BELT_STATS_FLOATS=64 };
enum { DANGO_BELT_CALIBRATION_FLOATS=11,DANGO_BELT_DIAGNOSTIC_FLOATS=48 };
/* Optional, additive ABI. Arrays and their units are described by schema JSON. */
DANGO_BELT_API const char* dango_belt_calibration_schema(void);
DANGO_BELT_API int dango_belt_create_calibrated(const dango_belt_config*,float,float,int,const float*,int,void**);
DANGO_BELT_API int dango_belt_read_diagnostics(void*,float*,int);
DANGO_BELT_API int dango_belt_set_original_ground_jump(void*,int);
enum { DANGO_BELT_SHAPE_GUARD_FLOATS=24 };
/* Opt-in before first successful step. Default disabled preserves legacy.
   -605: rejected guarded frame; -606: bounded certificate work exhausted.
   q/v and original read/energy diagnostics roll back atomically. Only the new
   attempt diagnostics survive failure, so callers can distinguish failures.
   [0]schema1 [1]enabled [2]attemptTick [3]lastReturn [4]work [5]calls
   [6]maxDepth [7]leaves [8]refusals [9]lastRefusalReason [10]stage
   [11]crossLower [12]clearanceLower [13]winding [14..15]guardElasticBefore/After
   [16]peakCoreReaction [17]guardEndpointWork [18]rigidGroundFallbacks
   [19]hardClearance [20]hardCross [21]workBudget [22]substeps [23]reserved.
   Reasons 1 cross,2 clearance,3 budget,4 winding,5 nonfinite,6 solve,7 ground.
   Stages 1 Newton,2 integrated path,3 ground,4 initial frame certificate. */
DANGO_BELT_API int dango_belt_set_shape_guard(void*,int);
DANGO_BELT_API int dango_belt_read_shape_guard(void*,float*,int);
DANGO_BELT_API int dango_belt_version(void);
DANGO_BELT_API int dango_belt_create(const dango_belt_config*,float,float,int,void**);
DANGO_BELT_API int dango_belt_step(void*,float,const float*,int);
DANGO_BELT_API int dango_belt_read(void*,float*,int,float*,int,float*,int);
DANGO_BELT_API int dango_belt_set_bounds(void*,float,float);
DANGO_BELT_API int dango_belt_destroy(void*);
#endif

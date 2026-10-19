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
   Reasons 1 cross,2 clearance,3 global work budget,4 winding,5 nonfinite,
   6 solve,7 ground,8 local certificate uncertainty (bounded depth/stack),
   9 ground candidate outside the unchanged material domain.
   Stages 1 Newton,2 integrated path,3 ground,4 initial frame certificate. */
DANGO_BELT_API int dango_belt_set_shape_guard(void*,int);
DANGO_BELT_API int dango_belt_read_shape_guard(void*,float*,int);
/* Separate opt-in, before the first successful step; requires shape guard.
   All generalized velocities share one positive scale, positions unchanged.
   Original energy diagnostic[18] records its KE delta separately.
   Speed12: 0 schema1,1 enabled,2 attempt,3 limitedSubsteps,4 minFactor(default1),
   5 maxBeforeUpper,6 maxAfterUpper,7 deltaKE,8 deltaPx,9 deltaPy,10 deltaL,
   11 reserved zero. Rejected-attempt telemetry survives physical rollback. */
DANGO_BELT_API int dango_belt_set_speed_saturation(void*,int);
DANGO_BELT_API int dango_belt_read_speed_limit(void*,double*,int);
/* Optional pre-first-successful-step traction; requires shape guard. Default off.
   Whale16: 0 schema1,1 enabled,2 attempt,3 signedGain,4 activeSubsteps,
   5..7 maxAbs netFx/netFy/torque residual,8 shapeEndpointWork (subset of
   original external work, never added twice),9 maxPointTraction,
   10 maxGeneralizedForce,11 final suppressionBits (air1,drag2,jump4,
   W/S/residualRamp8,cancel16),12 gainBefore,13 gainAfter,14 guardWorkPeak,
   15 reserved0. Entire frame rolls back on failure except attempt[2]. */
DANGO_BELT_API int dango_belt_set_whale_drive(void*,int);
DANGO_BELT_API int dango_belt_read_whale_drive(void*,double*,int);
/* Optional A/D direction replacement, default off, before first successful
   step, requires shape guard and mutually exclusive with whale mode.
   Radial16: 0 schema3,1 enabled,2 attempt,3 activeSubsteps,4 lastPointCount8,
   5 lastOriginalMagnitudeBudget,6 lastAppliedMagnitudeSum (may differ),7 lastNetFx,
   8 lastNetFy,9 maxAbsActualTorqueAboutCore,10 maxHorizontalReferenceError,
   11 twoThirdsReferenceError,12 lastReferenceNetFx64,
   13 frameEndpointWorkSubset (original external work),14 guardWorkPeak,
   15 lastReferenceNetFy64. References are 64 equal material midpoint radial
   samples in the selected world half. Eight consecutive groups each retain
   summed Fx and two-thirds summed Fy, applied at their material midpoint; actual
   forces need not be radial and no torque correction or core reaction exists.
   Zero final budget clears 4..8/12/15; peaks cover the frame.
   Failed frames roll back all except attempt[2]. */
DANGO_BELT_API int dango_belt_set_radial_drive(void*,int);
DANGO_BELT_API int dango_belt_read_radial_drive(void*,double*,int);
/* Rear16 schema1: 0 version,1 radialEnabled,2 attempt,3 activeSubsteps,
   4 lastPointCount,5 requestedDown,6 appliedDown,7 netFx,8 netFy,
   9 maxAbsTorqueCore,10 maxBudgetError,11 sourceEightPushFy,
   12 endpointWorkSubset,13 guardWorkPeak,14 movementSign,15 bodyMidX.
   Existing endpoint P=scale*B(theta)+(1-scale)*Core, theta=2pi*i/16,
   selected by sign*(P.x-bodyMidX)<-1e-9*radius. Equal pure downward loads
   total abs(mainFy)/3. Both curve and core receive endpoint J^T shares.
   Zero final budget clears 4..8/11/14/15; frame peaks 3/9/10 remain.
   Same readonly/capacity/lifetime and failed-frame rollback as Radial16. */
DANGO_BELT_API int dango_belt_read_rear_drive(void*,double*,int);
/* Read-only last successful substep active-force evaluation, pre-integration.
   Ledger1024 header: 0 schema1,1 capacity1024,2 successful frame attemptTick,
   3 zero-based lastSubstep (-1 before step),4 rowCount (max97),5 sourceMask,
   6 rowWidth6,7 referenceIntegrationCount64 (not actual load count). Rows from8: x,y,fx,fy,sourceKind,material.
   Kind1 horizontal and kind2 vertical are exact resultants of 16 material bins,
   material=binIndex (not physical nodes); kind3 is actual projected whale point
   weight t/64, material=theta; kind4 actual drag point, material=theta.
   Kind5 eight actual grouped point loads (Fx preserved/Fy two-thirds), material=theta.
   Kind6 actual rear scaffold endpoint force, material=theta, mask32.
   SourceMask uses 1<<(kind-1). Gravity/contact omitted. Unused slots zero.
   Read capacity failure writes nothing. Failed frames retain previous ledger. */
DANGO_BELT_API int dango_belt_read_force_ledger(void*,double*,int);
/* Lab-only read-only reproducibility data; no public restore/mutation API.
   Snapshot200: 0 schema1,1 count200,2..37 q36,38..73 v36,
   74 radius,75 scale,76 left,77 right,78 ramp,79 cooldown,80 dragTheta,
   81 shortBones,82 grounded,83 dragging,84 originalJump,85 hasStepped,86 guard,
   87..127 config41 in struct order,128..138 calibration11,139 guardAttemptTick,
   140..175 mass36,176 speedSaturationEnabled,177 whaleDriveEnabled,178 whaleSignedGain,179 radialEnabled,180..199 reserved zero.
   JSON doubles preserve binary64.
   Solver24: 0 schema1,1 attemptTick,2 return,3 returnSite,4 substep,5 GNiteration,
   6 lineSearch,7 residual,8 objective,9 directional,10 trialObjective,11 factor,
   12 contactResidual,13 penetration,14 speed,15 stage,16 residualValidatedLineSearchStops,
   17 dampedBfgsUpdates,18 gaussNewtonResets,19 speedCertificateSamples,
   20 speedCoreOrCurveSampleMaximum,21 speedCertifiedUpper,
   22 lastRefinementCoarseUpper,23 lastRefinementCertifiedUpper.
   stage 1 forces,2 integration,3 contact,4 speed/bounds,5 final validation.
   returnSite: 1 initial residual,2 objective nonfinite,3 Cholesky,4 Armijo,
   5 GN residual cap,6 integrated-path guard,7 Newton work budget,
   8 ground work budget,9 rigid ground fallback guard,10 ground penetration,
   11 contact residual,12 contact energy,13 speed,14 bounds,15 final residual,
   16 area,17 stats finite,18 energy diagnostics finite,19 initial guard,
   20 region force,21 speed saturation nonfinite/unresolved,22 whale traction domain,23 radial direction/interval domain,24 rear endpoint domain,
   31 initial derivative,32 initial radial length,33 initial short edge,
   34 initial area,35 initial barrier material domain.
   Solver diagnostics retain the rejected attempt. */
DANGO_BELT_API int dango_belt_read_snapshot(void*,double*,int);
DANGO_BELT_API int dango_belt_read_solver_diagnostics(void*,double*,int);
DANGO_BELT_API int dango_belt_version(void);
DANGO_BELT_API int dango_belt_create(const dango_belt_config*,float,float,int,void**);
DANGO_BELT_API int dango_belt_step(void*,float,const float*,int);
DANGO_BELT_API int dango_belt_read(void*,float*,int,float*,int,float*,int);
DANGO_BELT_API int dango_belt_set_bounds(void*,float,float);
DANGO_BELT_API int dango_belt_destroy(void*);
/* Product identity is distinct from the unchanged source ABI integer.
   Capabilities: snapshot1,guard2,speed4,radial8,rear16,reconfigure32,shortBones64.
   Reconfigure contract and supported domain: dango_belt_product.md. */
DANGO_BELT_API int dango_belt_product_version(void);
DANGO_BELT_API const char* dango_belt_product_model(void);
DANGO_BELT_API const char* dango_belt_product_tuning(void);
DANGO_BELT_API int dango_belt_product_capabilities(void);
DANGO_BELT_API int dango_belt_reconfigure(void*,const dango_belt_config*,const float*,int);
#endif

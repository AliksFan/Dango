#ifndef DANGO_SELF_PHYSICS_INTERNAL_H
#define DANGO_SELF_PHYSICS_INTERNAL_H

#include <box2d/box2d.h>
#include <stdbool.h>

#if !defined(DANGO_API)
#if defined(_WIN32)
#define DANGO_API __declspec(dllexport)
#else
#define DANGO_API __attribute__((visibility("default")))
#endif
#endif

#if !defined(DANGO_SOFT_BODY_MAX_NODES)
#define DANGO_SOFT_BODY_MAX_NODES 16
#endif

typedef struct dango_self_physics_config
{
    int node_count;
    int substep_count;
    float radial_rest_length;
    float radial_minimum_ratio;
    float radial_maximum_ratio;
    float perimeter_minimum_ratio;
    float perimeter_maximum_ratio;
    float radial_hertz;
    float perimeter_hertz;
    float radial_damping;
    float perimeter_damping;
    float area_strength;
    float ground_deformation_strength;
    float repulsion_distance;
    float repulsion_strength;
    float repulsion_maximum_force;
    float core_exclusion_ratio;
    float angle_strength;
    float local_angle_balance_strength;
    float top_inward_force;
    float gravity;
    float core_mass;
    float node_mass;
    float drop_height;
    float ground_height;
    float node_radius;
    float friction;
    float restitution;
    float contact_hertz;
    float linear_damping;
    float drag_stiffness;
    float drag_damping;
    float drag_maximum_force;
    float horizontal_force;
    float maximum_horizontal_speed;
    float air_control_ratio;
    float downward_force;
    float jump_impulse;
    float jump_cooldown_seconds;
    float maximum_speed;
    float minimum_area;
} dango_self_physics_config;

typedef struct dango_self_soft_body
{
    b2WorldId world_id;
    b2BodyId ground_body_id;
    b2ShapeId ground_shape_id;
    b2BodyId core_body_id;
    b2ShapeId core_shape_id;
    b2BodyId node_body_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2ShapeId node_shape_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2JointId radial_joint_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2JointId perimeter_joint_ids[DANGO_SOFT_BODY_MAX_NODES];
    int node_count;
    int substep_count;
    int initial_box2d_bytes;
    int selected_drag_node;
    float radial_rest_length;
    float perimeter_rest_length;
    float target_area;
    float seconds_since_jump;
    bool initialized;
    bool drag_active;
    bool grounded;
#if defined(DANGO_LAB_ENVELOPE)
    float envelope_stats[32];
#endif
} dango_self_soft_body;

bool dango_self_config_valid(const dango_self_physics_config* config);
void dango_self_apply_ground_deformation_force(
    dango_self_soft_body* body,
    const dango_self_physics_config* config);
bool dango_self_limit_speed(dango_self_soft_body* body, float limit);
bool dango_self_solve_constraints(
    dango_self_soft_body* body,
    const dango_self_physics_config* config);
void dango_self_create_rig(
    dango_self_soft_body* body,
    const dango_self_physics_config* config);
int dango_self_world_create(
    const dango_self_physics_config* config,
    dango_self_soft_body** body);
void dango_self_destroy_rig(dango_self_soft_body* body);
void dango_self_initialize_at(
    dango_self_soft_body* body,
    const dango_self_physics_config* config,
    float anchor_x,
    float anchor_y);
int dango_self_apply_step_forces(
    dango_self_soft_body* body,
    const dango_self_physics_config* config,
    float small_time_step,
    int small_step,
    float horizontal_control,
    float downward_control,
    int jump_pressed,
    int drag_active,
    float drag_target_x,
    float drag_target_y);
int dango_self_read_frame(
    dango_self_soft_body* body,
    const dango_self_physics_config* config,
    float* points,
    int* point_count,
    int* ground_contact_count,
    float* signed_area,
    float* centroid_x,
    float* centroid_y,
    float* core_x,
    float* core_y,
    float* maximum_speed,
    int* grounded,
    int* selected_drag_node);

DANGO_API int dango_box2d_self_soft_body_create(
    const dango_self_physics_config* config,
    void** self_handle);
DANGO_API int dango_box2d_self_soft_body_destroy(void* self_handle, int* outstanding_bytes);

#endif

#ifndef DANGO_ROUND_BALL_TYPES_H
#define DANGO_ROUND_BALL_TYPES_H

#include <stdint.h>

#define DANGO_ROUND_BALL_ABI_VERSION 1

/* Optional ABI-1 capability DTOs. Ball positions, radius, velocities, contact
   normals and gaps use q=diag(4/3,1)p. Dango states retain the original p and
   unchanged 42-slot layout. No field uses pixels, DIP, or caller DPI. */
typedef struct dango_ball_definition
{
    double x,y,radius,mass,floor_friction,dango_friction,floor_restitution,dango_restitution;
} dango_ball_definition;

typedef struct dango_ball_state
{
    int id,enabled,sleeping;
    uint64_t scene_revision,step,integrations;
    double x,y,vx,vy,angle,omega;
} dango_ball_state;

typedef struct dango_ball_contact
{
    int object_id,touching,sample;
    uint64_t scene_revision,step;
    double fraction,nx,ny,gap,normal_impulse,tangent_impulse;
    double normal_speed_before,tangent_speed_before,ball_speed_before;
} dango_ball_contact;

typedef struct dango_ball_snapshot
{
    uint64_t scene_revision,step;
    dango_ball_state ball;
    dango_ball_contact dango_contact,floor_contact;
} dango_ball_snapshot;

/* A single published scene/step. Valid Dango rows are compacted in the same
   active-slot order as world_step, with unused IDs/states zero. Definition is
   zero when no ball exists. Reserved is zero. A fault only updates error_code;
   all scene state remains the last jointly published safe frame. */
typedef struct dango_round_ball_frame
{
    dango_ball_snapshot snapshot;
    dango_ball_definition definition;
    int error_code,contact_steps,body_count,reserved;
    int entity_ids[3];
    float states[126];
} dango_round_ball_frame;

#endif

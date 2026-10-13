// Reproducer: one body settles onto a static platform with rest sleep enabled.
#include <stdio.h>
#include "dango_physics_module.h"

int main(void)
{
    dango_self_physics_config config = {
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
    setvbuf(stdout, NULL, _IONBF, 0);
    void* world = NULL; int id = 0;
    int e = dango_physics_world_create(&config, 1, &world); printf("create %d\n", e);
    e = dango_physics_world_add(world, 0, 1.2f, &id); printf("add %d\n", e);
    e = dango_physics_world_set_static_platform(world, 0, -.1f, 3, .3f, .72f); printf("platform %d\n", e);
    float controls[6] = {0}, states[42]; int ids[1], count = 0, contacts = 0, latched = 0;
    for (int f = 0; f < 900; ++f)
    {
        e = dango_physics_world_step(world, 1.0f / 60, &id, controls, 1, ids, states, 42, &count, &contacts, &latched);
        if (f >= 0) { printf("frame %d e=%d latched=%d core=%.5f,%.5f\n", f, e, latched, states[32], states[33]); fflush(stdout); }
        if (e || latched) break;
    }
    e = dango_physics_world_clear_static_platform(world); printf("clear %d\n", e);
    for (int f = 0; f < 120; ++f)
    {
        e = dango_physics_world_step(world, 1.0f / 60, &id, controls, 1, ids, states, 42, &count, &contacts, &latched);
        if (e || latched) { printf("after clear frame %d e=%d latched=%d\n", f, e, latched); break; }
    }
    printf("done core=%.5f,%.5f\n", states[32], states[33]);
    dango_physics_world_destroy(world);
    return 0;
}

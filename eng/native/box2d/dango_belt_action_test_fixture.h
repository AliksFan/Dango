#ifndef DANGO_BELT_ACTION_TEST_FIXTURE_H
#define DANGO_BELT_ACTION_TEST_FIXTURE_H
static const dango_belt_config preview_config={
 .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
 .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
 .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.local_angle_balance_strength=1.7f,
 .gravity=9.8f,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,
 .friction=.72f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
 .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,.jump_cooldown_seconds=.45f,
 .maximum_speed=12,.minimum_area=.42f,.linear_damping=1.8f};
static const float preview_calibration[11]={1.4f,1,1,4,1.4f,1,1,1,1.8f,35,60};
#endif

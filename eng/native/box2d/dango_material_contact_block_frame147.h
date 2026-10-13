#ifndef DANGO_MATERIAL_CONTACT_BLOCK_FRAME147_H
#define DANGO_MATERIAL_CONTACT_BLOCK_FRAME147_H

// Fixed natural-landing outer frame 147, small step 9; no world/cache replay.
// Source: artifacts/round-material/contact-block-cap-native-default.json
// SHA256: 9386a609f4fc764dd367ccb82a16811bd5b275caccee220b026b2493b87c523b
#include "dango_material_contact_solver.h"

static const double dango_material_contact_frame147_step_seconds = 0.0010416625300422311;
static const dango_material_ground_receipt dango_material_contact_frame147_receipts[4] = {
    {10, 0.0, 0.0, 3.3941125869750977, 0.0},
    {11, 0.0015111386310309172, -0.0011837705969810486, 3.3941125869750977, 0.0024989712983369827},
    {12, 0.0015100489836186171, 0.0011813751189038157, 3.3941125869750977, 0.0024945943150669336},
    {13, 0.0, 0.0, 3.3941125869750977, 0.0},
};

static inline dango_constraint_state dango_material_contact_frame147_state(void)
{
    dango_constraint_state state = {0};
    state.radial_length = 0.680000007f;
    state.perimeter_length = 0.265322834f;
    state.radial_min = 0.649999976f;
    state.radial_max = 1.22000003f;
    state.perimeter_min = 0.730000019f;
    state.perimeter_max = 1.07000005f;
    state.radius = 0.0450000018f;
    state.ground = -0.720000029f;
    state.positions[0] = (b2Vec2){0.767313838f, -0.106846981f};
    state.velocities[0] = (b2Vec2){0.00195436529f, 0.00119732541f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){0.614685059f, 0.116819292f};
    state.velocities[1] = (b2Vec2){0.00020177706f, -0.000948096102f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){0.395815879f, 0.278437763f};
    state.velocities[2] = (b2Vec2){-0.000273270329f, -0.00238452875f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){0.137125835f, 0.36574018f};
    state.velocities[3] = (b2Vec2){-0.000113061484f, -0.00298929028f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-0.136264727f, 0.365903378f};
    state.velocities[4] = (b2Vec2){0.000181496885f, -0.00295700412f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-0.395059466f, 0.278879225f};
    state.velocities[5] = (b2Vec2){0.000292552199f, -0.00233650906f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){-0.61409235f, 0.117436945f};
    state.velocities[6] = (b2Vec2){0.000256931235f, -0.000201454808f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){-0.766909182f, -0.106056862f};
    state.velocities[7] = (b2Vec2){-0.00225551776f, 0.000851514342f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.801269352f, -0.372539759f};
    state.velocities[8] = (b2Vec2){-0.00545540638f, 0.0020717869f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.660666466f, -0.597182631f};
    state.velocities[9] = (b2Vec2){-0.00791499298f, 0.000319563638f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){-0.408925861f, -0.674468458f};
    state.velocities[10] = (b2Vec2){-0.00580313988f, -0.00755949318f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){-0.142338336f, -0.675000012f};
    state.velocities[11] = (b2Vec2){-0.00108516065f, -0.00127918331f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){0.141557112f, -0.675000012f};
    state.velocities[12] = (b2Vec2){0.00113628071f, -0.00111089787f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){0.408180088f, -0.674469292f};
    state.velocities[13] = (b2Vec2){0.00547842542f, -0.00777085451f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){0.660072029f, -0.59765166f};
    state.velocities[14] = (b2Vec2){0.00756488461f, 0.000385651452f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){0.801219523f, -0.373370618f};
    state.velocities[15] = (b2Vec2){0.00527850073f, 0.00197201455f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.000390297297f, -0.256413281f};
    state.velocities[16] = (b2Vec2){6.94169139e-05f, -0.00322983111f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}

#endif

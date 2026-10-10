#ifndef DANGO_MATERIAL_CONTACT_BLOCK_FRAME29_H
#define DANGO_MATERIAL_CONTACT_BLOCK_FRAME29_H

// Fixed mode-7 natural-landing input, outer frame 29, small step 12.
// Captured before failed scalar velocity adjudication; no Box2D cache replay.
// Source: artifacts/round-material/contact-candidate-native-default.json
// SHA256: 14b19730df1c0f2df40afa0c2162d4e1f7a72d77004054e8d207a16669b98e65
#include "dango_material_contact_solver.h"

static const double dango_material_contact_frame29_step_seconds = 0.0010416625300422311;
static const dango_material_ground_receipt dango_material_contact_frame29_receipts[4] = {
    {10, 0., 0., 3.3941125869750977, 0.},
    {11, 0.013448497280478477, 0.0025557214394211769, 3.3941125869750977, 0.018645882606506348},
    {12, 0.013146417215466499, -0.0024620273616164923, 3.3941125869750977, 0.018863316625356674},
    {13, 0., 0., 3.3941125869750977, 0.},
};

static inline dango_constraint_state dango_material_contact_frame29_state(void)
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
    state.positions[0] = (b2Vec2){0.695763469f, -0.141720906f};
    state.velocities[0] = (b2Vec2){0.401760578f, -0.575129986f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){0.57725209f, 0.0973851755f};
    state.velocities[1] = (b2Vec2){0.0737810135f, -0.718307137f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){0.387589872f, 0.289312601f};
    state.velocities[2] = (b2Vec2){0.0197808798f, -0.742160141f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){0.136319816f, 0.393267423f};
    state.velocities[3] = (b2Vec2){0.0164262969f, -0.667143285f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-0.136352181f, 0.393249959f};
    state.velocities[4] = (b2Vec2){-0.0167538784f, -0.666972399f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-0.387604952f, 0.289269328f};
    state.velocities[5] = (b2Vec2){-0.0202239137f, -0.741920412f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){-0.577249885f, 0.0973278284f};
    state.velocities[6] = (b2Vec2){-0.0738163814f, -0.717059135f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){-0.695732474f, -0.141761571f};
    state.velocities[7] = (b2Vec2){-0.402242124f, -0.574015677f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.744679868f, -0.401035041f};
    state.velocities[8] = (b2Vec2){-0.624718368f, -0.57102555f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.635948658f, -0.633088171f};
    state.velocities[9] = (b2Vec2){-0.19704555f, -0.372193724f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){-0.390724689f, -0.674486399f};
    state.velocities[10] = (b2Vec2){-0.0586371236f, -0.0982223302f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){-0.132740587f, -0.675000012f};
    state.velocities[11] = (b2Vec2){-0.000228327917f, -1.41177554e-06f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){0.132755727f, -0.675000012f};
    state.velocities[12] = (b2Vec2){-0.000228334917f, 1.35258404e-06f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){0.390375644f, -0.674486995f};
    state.velocities[13] = (b2Vec2){0.064074941f, -0.0982856899f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){0.635909498f, -0.633159339f};
    state.velocities[14] = (b2Vec2){0.194793195f, -0.372146666f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){0.744720757f, -0.401108861f};
    state.velocities[15] = (b2Vec2){0.62395072f, -0.571841657f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){7.780136e-06f, -0.253404379f};
    state.velocities[16] = (b2Vec2){-8.41990332e-05f, -0.547484577f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}

#endif

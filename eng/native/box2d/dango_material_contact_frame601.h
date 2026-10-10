#ifndef DANGO_MATERIAL_CONTACT_FRAME601_H
#define DANGO_MATERIAL_CONTACT_FRAME601_H

// Fixed forward control entry frame 601, small step 3; pure state fixture.
// Source: artifacts/round-material/drift-repair-cache-forward-failure.json
// SHA256: 0b4c44bc76cc5c30ec3e77a5811bad352cc3255be58e8482632bbde5112e0e6e
// All 93 state floats are round-trip literals checked against recorded bits.
#include "dango_material_contact_solver.h"

static const double dango_material_contact_frame601_step_seconds = 0.001041662530042231;
static const dango_material_ground_receipt dango_material_contact_frame601_receipts[4] = {
    {10, 0, 0, 0.7200000286102295, 0},
    {11, 0.001558902789838612, -0.0010271182982251048, 0.7200000286102295, 0.0030879732221364975},
    {12, 0.0015146963996812701, 0.0010332034435123205, 0.7200000286102295, 0.002855038270354271},
    {13, 0, 0, 0.7200000286102295, 0},
};

static inline dango_constraint_state dango_material_contact_frame601_state(void)
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
    state.positions[0] = (b2Vec2){0.767963588f, -0.108163625f};
    state.velocities[0] = (b2Vec2){0.0022296079f, -0.00285402476f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){0.614934564f, 0.115224451f};
    state.velocities[1] = (b2Vec2){0.0150146801f, -0.00969028939f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){0.395997316f, 0.276575446f};
    state.velocities[2] = (b2Vec2){0.0770418793f, -0.0212456509f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){0.137896612f, 0.364021778f};
    state.velocities[3] = (b2Vec2){0.315198272f, 0.0244237352f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-0.133356363f, 0.365295738f};
    state.velocities[4] = (b2Vec2){0.956293702f, 0.29180932f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-0.39452666f, 0.27806592f};
    state.velocities[5] = (b2Vec2){0.316996962f, 0.083393611f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){-0.614679098f, 0.117057621f};
    state.velocities[6] = (b2Vec2){0.0699985027f, 0.0444392711f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){-0.768389523f, -0.106085502f};
    state.velocities[7] = (b2Vec2){0.0116769122f, 0.0147498921f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.803334653f, -0.372619808f};
    state.velocities[8] = (b2Vec2){0.000842728477f, 0.00333487731f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.662614882f, -0.597318411f};
    state.velocities[9] = (b2Vec2){7.46769365e-05f, 0.000400882127f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){-0.410488576f, -0.67446506f};
    state.velocities[10] = (b2Vec2){0.000122097568f, -0.00750178564f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){-0.14336212f, -0.674999654f};
    state.velocities[11] = (b2Vec2){-0.000606300484f, -0.00183433457f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){0.140533447f, -0.675000012f};
    state.velocities[12] = (b2Vec2){-0.00049638527f, -0.000338210666f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){0.407580167f, -0.674468398f};
    state.velocities[13] = (b2Vec2){-4.66985912e-05f, -0.00828605518f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){0.66000253f, -0.598559499f};
    state.velocities[14] = (b2Vec2){-0.000270039513f, -5.82411394e-06f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){0.801922083f, -0.374727011f};
    state.velocities[15] = (b2Vec2){4.5916895e-05f, -0.000492586347f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.00141278992f, -0.256413132f};
    state.velocities[16] = (b2Vec2){0.000896723825f, -0.00234606047f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}

static const uint32_t dango_material_contact_frame601_float_bits[93] = {
    UINT32_C(0x3F2E147B), UINT32_C(0x3E87D865), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D3851EC), UINT32_C(0xBF3851EC),
    UINT32_C(0x3F449943), UINT32_C(0xBDDD84E4), UINT32_C(0x3B121E9D), UINT32_C(0xBB3B0A97), UINT32_C(0x42855556), UINT32_C(0x3F1D6C5A), UINT32_C(0x3DEBFACC), UINT32_C(0x3C760022),
    UINT32_C(0xBC1EC405), UINT32_C(0x42855556), UINT32_C(0x3ECAC029), UINT32_C(0x3E8D9B4C), UINT32_C(0x3D9DC822), UINT32_C(0xBCAE0B5C), UINT32_C(0x42855556), UINT32_C(0x3E0D34C5),
    UINT32_C(0x3EBA6110), UINT32_C(0x3EA161AB), UINT32_C(0x3CC81449), UINT32_C(0x42855556), UINT32_C(0xBE088E92), UINT32_C(0x3EBB080B), UINT32_C(0x3F74CFAA), UINT32_C(0x3E956808),
    UINT32_C(0x42855556), UINT32_C(0xBEC9FF66), UINT32_C(0x3E8E5EA8), UINT32_C(0x3EA24D6D), UINT32_C(0x3DAACA45), UINT32_C(0x42855556), UINT32_C(0xBF1D5B9C), UINT32_C(0x3DEFBBE8),
    UINT32_C(0x3D8F5B60), UINT32_C(0x3D3605F4), UINT32_C(0x42855556), UINT32_C(0xBF44B52D), UINT32_C(0xBDD9435B), UINT32_C(0x3C3F5085), UINT32_C(0x3C71A988), UINT32_C(0x42855556),
    UINT32_C(0xBF4DA757), UINT32_C(0xBEBEC806), UINT32_C(0x3A5CEA8D), UINT32_C(0x3B5A8DF5), UINT32_C(0x42855556), UINT32_C(0xBF29A121), UINT32_C(0xBF18E9DC), UINT32_C(0x389C9BE0),
    UINT32_C(0x39D22D7D), UINT32_C(0x42855556), UINT32_C(0xBED22B8F), UINT32_C(0xBF2CA9BE), UINT32_C(0x39000751), UINT32_C(0xBBF5D18A), UINT32_C(0x42855556), UINT32_C(0xBE12CD85),
    UINT32_C(0xBF2CCCC7), UINT32_C(0xBA1EF023), UINT32_C(0xBAF06E0E), UINT32_C(0x42855556), UINT32_C(0x3E0FE800), UINT32_C(0xBF2CCCCD), UINT32_C(0xBA021FDA), UINT32_C(0xB9B151DE),
    UINT32_C(0x42855556), UINT32_C(0x3ED0AE59), UINT32_C(0xBF2CA9F6), UINT32_C(0xB843DE3B), UINT32_C(0xBC07C23C), UINT32_C(0x42855556), UINT32_C(0x3F28F5ED), UINT32_C(0xBF193B32),
    UINT32_C(0xB98D9417), UINT32_C(0xB6C36CC2), UINT32_C(0x42855556), UINT32_C(0x3F4D4AC4), UINT32_C(0xBEBFDC38), UINT32_C(0x384096E4), UINT32_C(0xBA0120E9), UINT32_C(0x42855556),
    UINT32_C(0xBAB92D5D), UINT32_C(0xBE834895), UINT32_C(0x3A6B121E), UINT32_C(0xBB19C05D), UINT32_C(0x41055555),
};

// Fixed forward frame 2720 failed mode 1; pure state fixture.
// Source: artifacts/round-material/drift-repair-capacity-forward-result.json
// SHA256: df22668001c3b0c3d7c5879f586fee256b378c8a79cf0d6ae2cb60dae84400e0
static const double dango_material_contact_frame2720_step_seconds = 0.001041662530042231;
static const dango_material_ground_receipt dango_material_contact_frame2720_receipts[2] = {
    {8, 0.0010685945162549615, -0.0007693881052546203, 0.7200000286102295, 0.0021328842267394066},
    {9, 0.00028344194288365543, -0.00020407821284607053, 0.7200000286102295, 0.0005089356563985348},
};

static inline dango_constraint_state dango_material_contact_frame2720_state(void)
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
    state.positions[0] = (b2Vec2){-0.0343643278f, 0.659139335f};
    state.velocities[0] = (b2Vec2){-6.95062256f, 0.0997540727f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){-0.316482514f, 0.645131826f};
    state.velocities[1] = (b2Vec2){-6.79972792f, -1.56468379f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){-0.57613796f, 0.534750402f};
    state.velocities[2] = (b2Vec2){-6.11235762f, -2.91904902f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){-0.796144962f, 0.359905332f};
    state.velocities[3] = (b2Vec2){-5.14537239f, -3.8980329f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-0.958522797f, 0.132885784f};
    state.velocities[4] = (b2Vec2){-3.56684613f, -4.76126146f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-0.994558096f, -0.140205339f};
    state.velocities[5] = (b2Vec2){-0.851257801f, -4.76417255f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){-0.887474835f, -0.402889997f};
    state.velocities[6] = (b2Vec2){-0.366162568f, -4.49068928f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){-0.677993476f, -0.592011034f};
    state.velocities[7] = (b2Vec2){0.256051451f, -3.52239966f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.409627348f, -0.674999952f};
    state.velocities[8] = (b2Vec2){1.13764477f, 0.00244578184f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.125731051f, -0.675000012f};
    state.velocities[9] = (b2Vec2){1.15488076f, 0.00352340424f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){0.149590045f, -0.605758071f};
    state.velocities[10] = (b2Vec2){0.731734514f, 1.68456995f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){0.373937815f, -0.431976855f};
    state.velocities[11] = (b2Vec2){-0.0108092725f, 2.70169926f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){0.506700695f, -0.181860462f};
    state.velocities[12] = (b2Vec2){-1.69496584f, 3.59585738f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){0.529890835f, 0.0995514542f};
    state.velocities[13] = (b2Vec2){-3.53647733f, 3.7275877f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){0.431710243f, 0.363768816f};
    state.velocities[14] = (b2Vec2){-5.15929174f, 3.08403349f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){0.230473876f, 0.56134814f};
    state.velocities[15] = (b2Vec2){-6.38430071f, 1.77666724f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.221700266f, 0.0503954627f};
    state.velocities[16] = (b2Vec2){-3.10099888f, -1.07847321f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}

static const uint32_t dango_material_contact_frame2720_float_bits[93] = {
    UINT32_C(0x3F2E147B), UINT32_C(0x3E87D865), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D3851EC), UINT32_C(0xBF3851EC),
    UINT32_C(0xBD0CC19C), UINT32_C(0x3F28BD5B), UINT32_C(0xC0DE6B80), UINT32_C(0x3DCC4BDD), UINT32_C(0x42855556), UINT32_C(0xBEA209FF), UINT32_C(0x3F25275C), UINT32_C(0xC0D9975F),
    UINT32_C(0xBFC8478F), UINT32_C(0x42855556), UINT32_C(0xBF137DC7), UINT32_C(0x3F08E567), UINT32_C(0xC0C3986F), UINT32_C(0xC03AD1B3), UINT32_C(0x42855556), UINT32_C(0xBF4BD028),
    UINT32_C(0x3EB84583), UINT32_C(0xC0A4A6E4), UINT32_C(0xC079795F), UINT32_C(0x42855556), UINT32_C(0xBF7561C0), UINT32_C(0x3E081336), UINT32_C(0xC0644735), UINT32_C(0xC0985C41),
    UINT32_C(0x42855556), UINT32_C(0xBF7E9B5C), UINT32_C(0xBE0F91FD), UINT32_C(0xBF59EC08), UINT32_C(0xC098741A), UINT32_C(0x42855556), UINT32_C(0xBF63318D), UINT32_C(0xBECE4799),
    UINT32_C(0xBEBB79A9), UINT32_C(0xC08FB3BA), UINT32_C(0x42855556), UINT32_C(0xBF2D90FB), UINT32_C(0xBF178E09), UINT32_C(0x3E83192D), UINT32_C(0xC0616EFF), UINT32_C(0x42855556),
    UINT32_C(0xBED1BAAD), UINT32_C(0xBF2CCCCC), UINT32_C(0x3F919E58), UINT32_C(0x3B204969), UINT32_C(0x42855556), UINT32_C(0xBE00BFA4), UINT32_C(0xBF2CCCCD), UINT32_C(0x3F93D322),
    UINT32_C(0x3B66E8EA), UINT32_C(0x42855556), UINT32_C(0x3E192E22), UINT32_C(0xBF1B12F6), UINT32_C(0x3F3B52F4), UINT32_C(0x3FD79FFD), UINT32_C(0x42855556), UINT32_C(0x3EBF74C7),
    UINT32_C(0xBEDD2C12), UINT32_C(0xBC311960), UINT32_C(0x402CE8A4), UINT32_C(0x42855556), UINT32_C(0x3F01B723), UINT32_C(0xBE3A39A1), UINT32_C(0xBFD8F4A4), UINT32_C(0x40662287),
    UINT32_C(0x42855556), UINT32_C(0x3F07A6ED), UINT32_C(0x3DCBE1A2), UINT32_C(0xC06255A5), UINT32_C(0x406E90CC), UINT32_C(0x42855556), UINT32_C(0x3EDD0920), UINT32_C(0x3EBA3FE8),
    UINT32_C(0xC0A518EB), UINT32_C(0x404560CE), UINT32_C(0x42855556), UINT32_C(0x3E6C0158), UINT32_C(0x3F0FB483), UINT32_C(0xC0CC4C31), UINT32_C(0x3FE369D5), UINT32_C(0x42855556),
    UINT32_C(0xBE630565), UINT32_C(0x3D4E6B79), UINT32_C(0xC04676C4), UINT32_C(0xBF8A0B69), UINT32_C(0x41055555),
};

// Fixed forward frame 3322 repeated-mode failure; pure state fixture.
// Source: artifacts/round-material/drift-repair-v2-forward.json
// SHA256: 4437164bf5325fdbbe0967edd0ef7f67c407b4f5e1635a34e03742b4bb3b3192
// All 93 state floats are round-trip literals checked against recorded bits.
static const double dango_material_contact_frame3322_step_seconds = 0.001041662530042231;
static const dango_material_ground_receipt dango_material_contact_frame3322_receipts[3] = {
    {0, 0.0017822801601141691, -0.0012832417851313949, 0.7200000286102295, 0.0033129483927041292},
    {1, 0, 0, 0.7200000286102295, 0},
    {15, 0.0013227283488959074, -0.0009523644694127142, 0.7200000286102295, 0.0025742966681718826},
};

static inline dango_constraint_state dango_material_contact_frame3322_state(void)
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
    state.positions[0] = (b2Vec2){-0.306518674f, -0.674997866f};
    state.velocities[0] = (b2Vec2){2.89502192f, -1.86264515E-09f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){-0.0229446776f, -0.66149199f};
    state.velocities[1] = (b2Vec2){2.97120857f, 0.7107535f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){0.250266612f, -0.584324539f};
    state.velocities[2] = (b2Vec2){2.59093499f, 2.05775404f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){0.493926495f, -0.438626975f};
    state.velocities[3] = (b2Vec2){2.00241303f, 3.16655684f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){0.668053508f, -0.214401469f};
    state.velocities[4] = (b2Vec2){0.79257071f, 4.10576248f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){0.618607044f, 0.0213689916f};
    state.velocities[5] = (b2Vec2){1.41722393f, 4.08288908f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){0.393683881f, 0.137450188f};
    state.velocities[6] = (b2Vec2){1.12212181f, 3.04052329f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){0.153382272f, 0.234247983f};
    state.velocities[7] = (b2Vec2){0.85673821f, 1.69350696f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.0976506695f, 0.320700943f};
    state.velocities[8] = (b2Vec2){0.757357717f, 0.616072118f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.364934623f, 0.352184594f};
    state.velocities[9] = (b2Vec2){0.920347691f, 0.153516769f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){-0.62384516f, 0.274029404f};
    state.velocities[10] = (b2Vec2){1.04348373f, 0.239439875f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){-0.831204951f, 0.102286644f};
    state.velocities[11] = (b2Vec2){1.13507199f, 0.21996057f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){-0.951406121f, -0.135309845f};
    state.velocities[12] = (b2Vec2){1.16779613f, 0.173463091f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){-0.966002464f, -0.397737712f};
    state.velocities[13] = (b2Vec2){1.34904969f, 0.0505094416f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){-0.844978392f, -0.624954939f};
    state.velocities[14] = (b2Vec2){2.52082682f, 0.462501287f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){-0.590418816f, -0.675000012f};
    state.velocities[15] = (b2Vec2){2.90306067f, 0.00012246969f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.151792213f, -0.238690138f};
    state.velocities[16] = (b2Vec2){1.31373692f, 0.234131232f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}

static const uint32_t dango_material_contact_frame3322_float_bits[93] = {
    UINT32_C(0x3F2E147B), UINT32_C(0x3E87D865), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D3851EC), UINT32_C(0xBF3851EC),
    UINT32_C(0xBE9CF004), UINT32_C(0xBF2CCCA9), UINT32_C(0x4039480A), UINT32_C(0xB1000000), UINT32_C(0x42855556), UINT32_C(0xBCBBF67A), UINT32_C(0xBF29578A), UINT32_C(0x403E2848),
    UINT32_C(0x3F35F3F1), UINT32_C(0x42855556), UINT32_C(0x3E8022F2), UINT32_C(0xBF15964B), UINT32_C(0x4025D1E1), UINT32_C(0x4003B23E), UINT32_C(0x42855556), UINT32_C(0x3EFCE3EF),
    UINT32_C(0xBEE093B7), UINT32_C(0x40002789), UINT32_C(0x404AA8DE), UINT32_C(0x42855556), UINT32_C(0x3F2B058E), UINT32_C(0xBE5B8C0F), UINT32_C(0x3F4AE5EA), UINT32_C(0x40836268),
    UINT32_C(0x42855556), UINT32_C(0x3F1E5D08), UINT32_C(0x3CAF0E06), UINT32_C(0x3FB56798), UINT32_C(0x4082A707), UINT32_C(0x42855556), UINT32_C(0x3EC990EF), UINT32_C(0x3E0CBFBE),
    UINT32_C(0x3F8FA1B0), UINT32_C(0x404297EF), UINT32_C(0x42855556), UINT32_C(0x3E1D103E), UINT32_C(0x3E6FDEB4), UINT32_C(0x3F5B5332), UINT32_C(0x3FD8C4D6), UINT32_C(0x42855556),
    UINT32_C(0xBDC7FD13), UINT32_C(0x3EA432EA), UINT32_C(0x3F41E232), UINT32_C(0x3F1DB6E7), UINT32_C(0x42855556), UINT32_C(0xBEBAD8B6), UINT32_C(0x3EB4518A), UINT32_C(0x3F6B9BE8),
    UINT32_C(0x3E1D3380), UINT32_C(0x42855556), UINT32_C(0xBF1FB451), UINT32_C(0x3E8C4D95), UINT32_C(0x3F8590E0), UINT32_C(0x3E752FBA), UINT32_C(0x42855556), UINT32_C(0xBF54C9D9),
    UINT32_C(0x3DD17BA9), UINT32_C(0x3F914A0A), UINT32_C(0x3E613D58), UINT32_C(0x42855556), UINT32_C(0xBF738F5A), UINT32_C(0xBE0A8EAA), UINT32_C(0x3F957A58), UINT32_C(0x3E31A04F),
    UINT32_C(0x42855556), UINT32_C(0xBF774BF0), UINT32_C(0xBECBA447), UINT32_C(0x3FACADA9), UINT32_C(0x3D4EE2FD), UINT32_C(0x42855556), UINT32_C(0xBF585081), UINT32_C(0xBF1FFD0C),
    UINT32_C(0x4021553A), UINT32_C(0x3EECCCF8), UINT32_C(0x42855556), UINT32_C(0xBF1725B0), UINT32_C(0xBF2CCCCD), UINT32_C(0x4039CBBF), UINT32_C(0x39006B35), UINT32_C(0x42855556),
    UINT32_C(0xBE1B6F6B), UINT32_C(0xBE746B30), UINT32_C(0x3FA82888), UINT32_C(0x3E6FC019), UINT32_C(0x41055555),
};

// Fixed changing material 80/50 frame 1711 repeated-mode failure; pure state fixture.
// Source: artifacts/round-material/drift-repair-v3-changing-80-50.json
// SHA256: 6d20b1e09a7a2dcfe9e4385ed09e846de23ba48386c174ebe3a735fa0bcce1e9
// All 93 state floats are round-trip literals checked against recorded bits.
static const double dango_material_contact_frame1711_step_seconds = 0.001041662530042231;
static const dango_material_ground_receipt dango_material_contact_frame1711_receipts[5] = {
    {2, 1.4805794307903852E-05, -5.0252532673766837E-05, 3.3941125869750977, 1.4805794307903852E-05},
    {3, 0.00213277037255466, -0.0015022268053144217, 3.3941125869750977, 0.003601172473281622},
    {4, 0.00047192053170874715, 0.0011451572645455599, 3.3941125869750977, 0.0008894331404007971},
    {5, 0.0011008292203769088, 0.0005012940964661539, 3.3941125869750977, 0.0011008292203769088},
    {6, 0, 0, 3.3941125869750977, 0},
};

static inline dango_constraint_state dango_material_contact_frame1711_state(void)
{
    dango_constraint_state state = {0};
    state.radial_length = 0.54400003f;
    state.perimeter_length = 0.212258279f;
    state.radial_min = 0.649999976f;
    state.radial_max = 1.22000003f;
    state.perimeter_min = 0.730000019f;
    state.perimeter_max = 1.07000005f;
    state.radius = 0.0359999985f;
    state.ground = -0.720000029f;
    state.positions[0] = (b2Vec2){-2.16584468f, -0.426939815f};
    state.velocities[0] = (b2Vec2){-1.31919158f, -3.60473919f};
    state.inverse_mass[0] = 104.166664f;
    state.positions[1] = (b2Vec2){-2.13680339f, -0.629181743f};
    state.velocities[1] = (b2Vec2){-0.41012302f, -3.23041177f};
    state.inverse_mass[1] = 104.166664f;
    state.positions[2] = (b2Vec2){-1.95607328f, -0.68351686f};
    state.velocities[2] = (b2Vec2){0.267374218f, -0.263815284f};
    state.inverse_mass[2] = 104.166664f;
    state.positions[3] = (b2Vec2){-1.74930716f, -0.683963954f};
    state.velocities[3] = (b2Vec2){0.0325634107f, 0.000828500837f};
    state.inverse_mass[3] = 104.166664f;
    state.positions[4] = (b2Vec2){-1.52368379f, -0.684000015f};
    state.velocities[4] = (b2Vec2){0.0325632282f, -0.000336494733f};
    state.inverse_mass[4] = 104.166664f;
    state.positions[5] = (b2Vec2){-1.32279515f, -0.683632255f};
    state.velocities[5] = (b2Vec2){0.0270509496f, -0.105886526f};
    state.inverse_mass[5] = 104.166664f;
    state.positions[6] = (b2Vec2){-1.13366485f, -0.682908177f};
    state.velocities[6] = (b2Vec2){-0.18461673f, -0.00247431686f};
    state.inverse_mass[6] = 104.166664f;
    state.positions[7] = (b2Vec2){-1.00694847f, -0.550683081f};
    state.velocities[7] = (b2Vec2){2.03070617f, -3.15831232f};
    state.inverse_mass[7] = 104.166664f;
    state.positions[8] = (b2Vec2){-1.01620424f, -0.348957479f};
    state.velocities[8] = (b2Vec2){0.929905593f, -3.66887164f};
    state.inverse_mass[8] = 104.166664f;
    state.positions[9] = (b2Vec2){-1.0738728f, -0.14656812f};
    state.velocities[9] = (b2Vec2){0.309491366f, -4.14405394f};
    state.inverse_mass[9] = 104.166664f;
    state.positions[10] = (b2Vec2){-1.20556521f, 0.026046291f};
    state.velocities[10] = (b2Vec2){0.516450644f, -4.02899885f};
    state.inverse_mass[10] = 104.166664f;
    state.positions[11] = (b2Vec2){-1.39869475f, 0.134622663f};
    state.velocities[11] = (b2Vec2){0.423586398f, -3.87387371f};
    state.inverse_mass[11] = 104.166664f;
    state.positions[12] = (b2Vec2){-1.62004662f, 0.162136123f};
    state.velocities[12] = (b2Vec2){0.177799717f, -3.6862011f};
    state.inverse_mass[12] = 104.166664f;
    state.positions[13] = (b2Vec2){-1.83412325f, 0.10231071f};
    state.velocities[13] = (b2Vec2){-0.14940317f, -3.59527922f};
    state.inverse_mass[13] = 104.166664f;
    state.positions[14] = (b2Vec2){-2.00647926f, -0.0342347957f};
    state.velocities[14] = (b2Vec2){-0.261541337f, -3.83612943f};
    state.inverse_mass[14] = 104.166664f;
    state.positions[15] = (b2Vec2){-2.11315203f, -0.222211495f};
    state.velocities[15] = (b2Vec2){-0.236785218f, -3.9659934f};
    state.inverse_mass[15] = 104.166664f;
    state.positions[16] = (b2Vec2){-1.60865104f, -0.340234607f};
    state.velocities[16] = (b2Vec2){-0.369055808f, -0.255846679f};
    state.inverse_mass[16] = 13.0208311f;
    return state;
}

static const uint32_t dango_material_contact_frame1711_float_bits[93] = {
    UINT32_C(0x3F0B4396), UINT32_C(0x3E595A3C), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D1374BC), UINT32_C(0xBF3851EC),
    UINT32_C(0xC00A9D33), UINT32_C(0xBEDA97DB), UINT32_C(0xBFA8DB45), UINT32_C(0xC066B40C), UINT32_C(0x42D05555), UINT32_C(0xC008C163), UINT32_C(0xBF21120E), UINT32_C(0xBED1FBA5),
    UINT32_C(0xC04EBF11), UINT32_C(0x42D05555), UINT32_C(0xBFFA609C), UINT32_C(0xBF2EFAF6), UINT32_C(0x3E88E546), UINT32_C(0xBE8712CC), UINT32_C(0x42D05555), UINT32_C(0xBFDFE94C),
    UINT32_C(0xBF2F1843), UINT32_C(0x3D056136), UINT32_C(0x3A592FC0), UINT32_C(0x42D05555), UINT32_C(0xBFC30812), UINT32_C(0xBF2F1AA0), UINT32_C(0x3D056105), UINT32_C(0xB9B06B8F),
    UINT32_C(0x42D05555), UINT32_C(0xBFA9515A), UINT32_C(0xBF2F0286), UINT32_C(0x3CDD99F4), UINT32_C(0xBDD8DB09), UINT32_C(0x42D05555), UINT32_C(0xBF911BEE), UINT32_C(0xBF2ED312),
    UINT32_C(0xBE3D0C2B), UINT32_C(0xBB222826), UINT32_C(0x42D05555), UINT32_C(0xBF80E3B0), UINT32_C(0xBF0CF991), UINT32_C(0x4001F717), UINT32_C(0xC04A21CA), UINT32_C(0x42D05555),
    UINT32_C(0xBF8212FB), UINT32_C(0xBEB2AA8E), UINT32_C(0x3F6E0E4B), UINT32_C(0xC06ACECB), UINT32_C(0x42D05555), UINT32_C(0xBF8974AA), UINT32_C(0xBE1615F4), UINT32_C(0x3E9E75A7),
    UINT32_C(0xC0849C17), UINT32_C(0x42D05555), UINT32_C(0xBF9A4FF6), UINT32_C(0x3CD55F08), UINT32_C(0x3F04361C), UINT32_C(0xC080ED8F), UINT32_C(0x42D05555), UINT32_C(0xBFB3086E),
    UINT32_C(0x3E09DA86), UINT32_C(0x3ED8E051), UINT32_C(0xC077ED8C), UINT32_C(0x42D05555), UINT32_C(0xBFCF5DB0), UINT32_C(0x3E260703), UINT32_C(0x3E361121), UINT32_C(0xC06BEAB8),
    UINT32_C(0x42D05555), UINT32_C(0xBFEAC48D), UINT32_C(0x3DD18847), UINT32_C(0xBE18FD25), UINT32_C(0xC066190E), UINT32_C(0x42D05555), UINT32_C(0xC0006A28), UINT32_C(0xBD0C39C9),
    UINT32_C(0xBE85E8BF), UINT32_C(0xC0758325), UINT32_C(0x42D05555), UINT32_C(0xC0073DE2), UINT32_C(0xBE638B69), UINT32_C(0xBE7277D3), UINT32_C(0xC07DD2D6), UINT32_C(0x42D05555),
    UINT32_C(0xBFCDE847), UINT32_C(0xBEAE333B), UINT32_C(0xBEBCF4E2), UINT32_C(0xBE82FE56), UINT32_C(0x41505553),
};
#endif

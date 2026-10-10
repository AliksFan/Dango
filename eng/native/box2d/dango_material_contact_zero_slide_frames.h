#ifndef DANGO_MATERIAL_CONTACT_ZERO_SLIDE_FRAMES_H
#define DANGO_MATERIAL_CONTACT_ZERO_SLIDE_FRAMES_H

// Exact captured damped entry states; no legal-mode seed or world runtime.
#include "dango_material_contact_solver.h"

// Source: artifacts/round-material/drift-repair-v10-audit-horizontal-100-100.json
// SHA256: 5840ecda4a25e97a8d0d95ddd46573a953fca356c2e9f7d2e734a85e82027a2c
// Attempted frame 2886; state has 93 exact float values.
static const double dango_material_contact_horizontal2886_step = 0.0010416625300422311;
static const uint64_t dango_material_contact_horizontal2886_step_bits = UINT64_C(0x3F51110CA0000000);
static const dango_material_ground_receipt dango_material_contact_horizontal2886_receipts[2] = {
    {11, 0.0, 0.0, 3.3941125869750977, 0.0},
    {12, 0.0014741000486537814, 0.0038360103499144316, 3.3941125869750977, 0.0027750474400818348},
};
static const uint64_t dango_material_contact_horizontal2886_receipt_bits[2][4] = {
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x400B272480000000), UINT64_C(0x0000000000000000)},
    {UINT64_C(0x3F5826D2E0000000), UINT64_C(0x3F6F6CB260000000), UINT64_C(0x400B272480000000), UINT64_C(0x3F66BBB240000000)},
};

static inline dango_constraint_state dango_material_contact_horizontal2886_state(void)
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
    state.positions[0] = (b2Vec2){0.588933885f, 0.180701926f};
    state.velocities[0] = (b2Vec2){1.01891553f, -1.76327085f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){0.570700347f, 0.436140895f};
    state.velocities[1] = (b2Vec2){1.35604584f, -1.5355711f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){0.452656388f, 0.663702011f};
    state.velocities[2] = (b2Vec2){0.551406145f, -1.63381433f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){0.227765188f, 0.783346176f};
    state.velocities[3] = (b2Vec2){0.435159266f, -1.18779838f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-0.044169914f, 0.739243805f};
    state.velocities[4] = (b2Vec2){0.361587733f, -0.487722486f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-0.280462712f, 0.589095056f};
    state.velocities[5] = (b2Vec2){0.122305341f, 0.0386428498f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){-0.473209739f, 0.384370118f};
    state.velocities[6] = (b2Vec2){0.0814194009f, 0.212456688f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){-0.618227601f, 0.143019855f};
    state.velocities[7] = (b2Vec2){-0.103432402f, 0.431490541f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.666203618f, -0.133970127f};
    state.velocities[8] = (b2Vec2){-0.454054296f, 0.566088796f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.566460371f, -0.397542864f};
    state.velocities[9] = (b2Vec2){-0.667265177f, 0.438979596f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){-0.351084679f, -0.582506239f};
    state.velocities[10] = (b2Vec2){-0.692217886f, 0.409843594f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){-0.0826768354f, -0.674993217f};
    state.velocities[11] = (b2Vec2){-0.813258767f, -0.00490310695f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){0.192433342f, -0.675000012f};
    state.velocities[12] = (b2Vec2){-0.0143728526f, -0.000360269507f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){0.408352762f, -0.555075824f};
    state.velocities[13] = (b2Vec2){-0.26483202f, -1.1645391f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){0.517739534f, -0.327591568f};
    state.velocities[14] = (b2Vec2){-0.521875799f, -1.49970043f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){0.568003118f, -0.0754583329f};
    state.velocities[15] = (b2Vec2){0.190392658f, -1.76847517f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.0452368334f, 0.0321103483f};
    state.velocities[16] = (b2Vec2){-0.0732403398f, -0.436239839f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}
static const uint32_t dango_material_contact_horizontal2886_float_bits[93] = {
    UINT32_C(0x3F2E147B), UINT32_C(0x3E87D865), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D3851EC), UINT32_C(0xBF3851EC),
    UINT32_C(0x3F16C45F), UINT32_C(0x3E3909ED), UINT32_C(0x3F826BD3), UINT32_C(0xBFE1B2DC), UINT32_C(0x42855556), UINT32_C(0x3F12196B), UINT32_C(0x3EDF4DDC), UINT32_C(0x3FAD92E9),
    UINT32_C(0xBFC48D98), UINT32_C(0x42855556), UINT32_C(0x3EE7C294), UINT32_C(0x3F29E860), UINT32_C(0x3F0D28F4), UINT32_C(0xBFD120D4), UINT32_C(0x42855556), UINT32_C(0x3E693B47),
    UINT32_C(0x3F488960), UINT32_C(0x3EDECD32), UINT32_C(0xBF9809C7), UINT32_C(0x42855556), UINT32_C(0xBD34EB83), UINT32_C(0x3F3D3F15), UINT32_C(0x3EB92207), UINT32_C(0xBEF9B6C3),
    UINT32_C(0x42855556), UINT32_C(0xBE8F98CF), UINT32_C(0x3F16CEEF), UINT32_C(0x3DFA7B39), UINT32_C(0x3D1E47F7), UINT32_C(0x42855556), UINT32_C(0xBEF2488C), UINT32_C(0x3EC4CC29),
    UINT32_C(0x3DA6BF37), UINT32_C(0x3E598E3F), UINT32_C(0x42855556), UINT32_C(0xBF1E442A), UINT32_C(0x3E1273CC), UINT32_C(0xBDD3D45E), UINT32_C(0x3EDCEC54), UINT32_C(0x42855556),
    UINT32_C(0xBF2A8C52), UINT32_C(0xBE092F77), UINT32_C(0xBEE879CE), UINT32_C(0x3F10EB32), UINT32_C(0x42855556), UINT32_C(0xBF11038C), UINT32_C(0xBECB8ABD), UINT32_C(0xBF2AD1E4),
    UINT32_C(0x3EE0C1EF), UINT32_C(0x42855556), UINT32_C(0xBEB3C15F), UINT32_C(0xBF151F21), UINT32_C(0xBF313531), UINT32_C(0x3ED1D705), UINT32_C(0x42855556), UINT32_C(0xBDA95279),
    UINT32_C(0xBF2CCC5B), UINT32_C(0xBF5031BA), UINT32_C(0xBBA0AA3E), UINT32_C(0x42855556), UINT32_C(0x3E450D3F), UINT32_C(0xBF2CCCCD), UINT32_C(0xBC6B7C1D), UINT32_C(0xB9BCE28E),
    UINT32_C(0x42855556), UINT32_C(0x3ED1139D), UINT32_C(0xBF0E1973), UINT32_C(0xBE879810), UINT32_C(0xBF950F9E), UINT32_C(0x42855556), UINT32_C(0x3F048A94), UINT32_C(0xBEA7BA15),
    UINT32_C(0xBF0599A7), UINT32_C(0xBFBFF62F), UINT32_C(0x42855556), UINT32_C(0x3F1168A7), UINT32_C(0xBD9A89E6), UINT32_C(0x3E42F64B), UINT32_C(0xBFE25D65), UINT32_C(0x42855556),
    UINT32_C(0xBD394A42), UINT32_C(0x3D038624), UINT32_C(0xBD95FF08), UINT32_C(0xBEDF5AD4), UINT32_C(0x41055555),
};

// Source: artifacts/round-material/drift-repair-v10-audit-changing-120-100.json
// SHA256: 50050d678816b6aefa1737caa0039839ed6b901276e2d2325f080589f9e4989c
// Attempted frame 4896; state has 93 exact float values.
static const double dango_material_contact_changing4896_step = 0.0010416625300422311;
static const uint64_t dango_material_contact_changing4896_step_bits = UINT64_C(0x3F51110CA0000000);
static const dango_material_ground_receipt dango_material_contact_changing4896_receipts[2] = {
    {9, 0.0023414420429617167, -0.0016858383314684033, 0.72000002861022949, 0.0044172275811433792},
    {10, 0.0, 0.0, 0.72000002861022949, 0.0},
};
static const uint64_t dango_material_contact_changing4896_receipt_bits[2][4] = {
    {UINT64_C(0x3F632E5C20000000), UINT64_C(0xBF5B9EEB20000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x3F7217CC80000000)},
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x0000000000000000)},
};

static inline dango_constraint_state dango_material_contact_changing4896_state(void)
{
    dango_constraint_state state = {0};
    state.radial_length = 0.815999985f;
    state.perimeter_length = 0.318387389f;
    state.radial_min = 0.649999976f;
    state.radial_max = 1.22000003f;
    state.perimeter_min = 0.730000019f;
    state.perimeter_max = 1.07000005f;
    state.radius = 0.0540000014f;
    state.ground = -0.720000029f;
    state.positions[0] = (b2Vec2){-0.712463677f, 0.84595269f};
    state.velocities[0] = (b2Vec2){-0.631756365f, 1.16088176f};
    state.inverse_mass[0] = 46.2962914f;
    state.positions[1] = (b2Vec2){-1.01130903f, 0.912304401f};
    state.velocities[1] = (b2Vec2){-0.779320538f, 0.699413955f};
    state.inverse_mass[1] = 46.2962914f;
    state.positions[2] = (b2Vec2){-1.32075548f, 0.852083862f};
    state.velocities[2] = (b2Vec2){-0.661652863f, -0.0373814851f};
    state.inverse_mass[2] = 46.2962914f;
    state.positions[3] = (b2Vec2){-1.62516737f, 0.744331479f};
    state.velocities[3] = (b2Vec2){-0.265100509f, -1.13944256f};
    state.inverse_mass[3] = 46.2962914f;
    state.positions[4] = (b2Vec2){-1.90353882f, 0.574916005f};
    state.velocities[4] = (b2Vec2){0.308331668f, -2.04623675f};
    state.inverse_mass[4] = 46.2962914f;
    state.positions[5] = (b2Vec2){-2.08946276f, 0.308403373f};
    state.velocities[5] = (b2Vec2){0.971141636f, -2.43215632f};
    state.inverse_mass[5] = 46.2962914f;
    state.positions[6] = (b2Vec2){-2.12845039f, -0.0113176219f};
    state.velocities[6] = (b2Vec2){0.92217797f, -2.33887839f};
    state.inverse_mass[6] = 46.2962914f;
    state.positions[7] = (b2Vec2){-2.09338021f, -0.326323807f};
    state.velocities[7] = (b2Vec2){0.557832718f, -2.29786444f};
    state.inverse_mass[7] = 46.2962914f;
    state.positions[8] = (b2Vec2){-1.9411341f, -0.595526218f};
    state.velocities[8] = (b2Vec2){1.98168623f, -1.42067719f};
    state.inverse_mass[8] = 46.2962914f;
    state.positions[9] = (b2Vec2){-1.64779627f, -0.666000009f};
    state.velocities[9] = (b2Vec2){2.34317207f, -0.000130027693f};
    state.inverse_mass[9] = 46.2962914f;
    state.positions[10] = (b2Vec2){-1.31905532f, -0.665991902f};
    state.velocities[10] = (b2Vec2){2.67783737f, -0.00404073345f};
    state.inverse_mass[10] = 46.2962914f;
    state.positions[11] = (b2Vec2){-1.00671208f, -0.529972196f};
    state.velocities[11] = (b2Vec2){2.33850098f, 0.816242874f};
    state.inverse_mass[11] = 46.2962914f;
    state.positions[12] = (b2Vec2){-0.808091164f, -0.253188342f};
    state.velocities[12] = (b2Vec2){2.04487085f, 1.02746832f};
    state.inverse_mass[12] = 46.2962914f;
    state.positions[13] = (b2Vec2){-0.701534569f, 0.0703929216f};
    state.velocities[13] = (b2Vec2){1.2048614f, 1.32469332f};
    state.inverse_mass[13] = 46.2962914f;
    state.positions[14] = (b2Vec2){-0.612467706f, 0.342695028f};
    state.velocities[14] = (b2Vec2){0.959875762f, 1.32097578f};
    state.inverse_mass[14] = 46.2962914f;
    state.positions[15] = (b2Vec2){-0.530112803f, 0.619978368f};
    state.velocities[15] = (b2Vec2){0.0195050146f, 1.60171926f};
    state.inverse_mass[15] = 46.2962914f;
    state.positions[16] = (b2Vec2){-1.26429212f, 0.0173723549f};
    state.velocities[16] = (b2Vec2){1.34368908f, -0.154721007f};
    state.inverse_mass[16] = 5.7870369f;
    return state;
}
static const uint32_t dango_material_contact_changing4896_float_bits[93] = {
    UINT32_C(0x3F50E560), UINT32_C(0x3EA303AC), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D5D2F1B), UINT32_C(0xBF3851EC),
    UINT32_C(0xBF366405), UINT32_C(0x3F58905B), UINT32_C(0xBF21BAC9), UINT32_C(0x3F9497C6), UINT32_C(0x42392F67), UINT32_C(0xBF817293), UINT32_C(0x3F698CC8), UINT32_C(0xBF47818D),
    UINT32_C(0x3F330CCB), UINT32_C(0x42392F67), UINT32_C(0xBFA90E84), UINT32_C(0x3F5A222B), UINT32_C(0xBF296215), UINT32_C(0xBD191D54), UINT32_C(0x42392F67), UINT32_C(0xBFD0057C),
    UINT32_C(0x3F3E8C82), UINT32_C(0xBE87BB41), UINT32_C(0xBF91D941), UINT32_C(0x42392F67), UINT32_C(0xBFF3A729), UINT32_C(0x3F132DB2), UINT32_C(0x3E9DDDA6), UINT32_C(0xC002F58B),
    UINT32_C(0x42392F67), UINT32_C(0xC005B9C2), UINT32_C(0x3E9DE70C), UINT32_C(0x3F789CBD), UINT32_C(0xC01BA873), UINT32_C(0x42392F67), UINT32_C(0xC0083888), UINT32_C(0xBC396D8C),
    UINT32_C(0x3F6C13DB), UINT32_C(0xC015B02F), UINT32_C(0x42392F67), UINT32_C(0xC005F9F1), UINT32_C(0xBEA713EA), UINT32_C(0x3F0ECE20), UINT32_C(0xC0131036), UINT32_C(0x42392F67),
    UINT32_C(0xBFF87715), UINT32_C(0xBF187468), UINT32_C(0x3FFDA7E5), UINT32_C(0xBFB5D8C0), UINT32_C(0x42392F67), UINT32_C(0xBFD2EAFD), UINT32_C(0xBF2A7EFA), UINT32_C(0x4015F688),
    UINT32_C(0xB908580B), UINT32_C(0x42392F67), UINT32_C(0xBFA8D6CE), UINT32_C(0xBF2A7E72), UINT32_C(0x402B61B0), UINT32_C(0xBB846821), UINT32_C(0x42392F67), UINT32_C(0xBF80DBF1),
    UINT32_C(0xBF07AC42), UINT32_C(0x4015AA00), UINT32_C(0x3F50F54B), UINT32_C(0x42392F67), UINT32_C(0xBF4EDF10), UINT32_C(0xBE81A1E7), UINT32_C(0x4002DF2A), UINT32_C(0x3F838415),
    UINT32_C(0x42392F67), UINT32_C(0xBF3397C5), UINT32_C(0x3D902A2A), UINT32_C(0x3F9A38E6), UINT32_C(0x3FA98F8D), UINT32_C(0x42392F67), UINT32_C(0xBF1CCAAF), UINT32_C(0x3EAF75B9),
    UINT32_C(0x3F75BA6B), UINT32_C(0x3FA915BC), UINT32_C(0x42392F67), UINT32_C(0xBF07B579), UINT32_C(0x3F1EB6E7), UINT32_C(0x3C9FC8FB), UINT32_C(0x3FCD0523), UINT32_C(0x42392F67),
    UINT32_C(0xBFA1D453), UINT32_C(0x3C8E5078), UINT32_C(0x3FABFE01), UINT32_C(0xBE1E6F2F), UINT32_C(0x40B92F68),
};

// Source: artifacts/round-material/drift-repair-v13-relaxed-wide-forward.json
// SHA256: f85528d722356dec548c9146564609422ea76f6c97a118fd95f5c255423fbcee
// Attempted frame 2255; state has 93 exact float values.
static const double dango_material_contact_relaxed2255_step = 0.0010416625300422311;
static const uint64_t dango_material_contact_relaxed2255_step_bits = UINT64_C(0x3F51110CA0000000);
static const dango_material_ground_receipt dango_material_contact_relaxed2255_receipts[2] = {
    {2, 0.0, 0.0, 0.72000002861022949, 0.0},
    {3, 0.0, 0.0, 0.72000002861022949, 0.0},
};
static const uint64_t dango_material_contact_relaxed2255_receipt_bits[2][4] = {
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x0000000000000000)},
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x0000000000000000)},
};

static inline dango_constraint_state dango_material_contact_relaxed2255_state(void)
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
    state.positions[0] = (b2Vec2){-1.26652503f, -0.282027125f};
    state.velocities[0] = (b2Vec2){0.603183568f, 3.63641262f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){-1.11727118f, -0.523528516f};
    state.velocities[1] = (b2Vec2){-0.726199627f, 2.79162335f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){-0.865405142f, -0.654527724f};
    state.velocities[2] = (b2Vec2){-1.55300319f, 1.20183218f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){-0.582248569f, -0.674996972f};
    state.velocities[3] = (b2Vec2){-1.63787007f, -0.00169708463f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-0.308372408f, -0.641031563f};
    state.velocities[4] = (b2Vec2){-1.46825552f, 0.0905393139f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-0.0588150062f, -0.531796157f};
    state.velocities[5] = (b2Vec2){-1.34051335f, 0.147458285f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){0.114157379f, -0.325464725f};
    state.velocities[6] = (b2Vec2){-1.01751125f, -0.0318743661f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){0.163201347f, -0.0637349337f};
    state.velocities[7] = (b2Vec2){-0.191315114f, -0.182576448f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){0.0856306329f, 0.185900807f};
    state.velocities[8] = (b2Vec2){0.565692008f, -0.00858840998f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.0962003991f, 0.362130284f};
    state.velocities[9] = (b2Vec2){0.904035449f, 0.188125879f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){-0.321638167f, 0.455941707f};
    state.velocities[10] = (b2Vec2){1.43287158f, 1.21001649f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){-0.544885874f, 0.527556241f};
    state.velocities[11] = (b2Vec2){1.88724339f, 2.29273558f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){-0.765235364f, 0.572685719f};
    state.velocities[12] = (b2Vec2){2.20865679f, 3.34899497f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){-0.955423951f, 0.469913363f};
    state.velocities[13] = (b2Vec2){2.50706339f, 3.12633467f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){-1.14539707f, 0.258945048f};
    state.velocities[14] = (b2Vec2){2.25258613f, 3.35552049f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){-1.26585412f, 0.00186753657f};
    state.velocities[15] = (b2Vec2){1.53285003f, 3.64207292f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.6205706f, -0.144660637f};
    state.velocities[16] = (b2Vec2){-0.193649024f, 1.14424872f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}
static const uint32_t dango_material_contact_relaxed2255_float_bits[93] = {
    UINT32_C(0x3F2E147B), UINT32_C(0x3E87D865), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D3851EC), UINT32_C(0xBF3851EC),
    UINT32_C(0xBFA21D7E), UINT32_C(0xBE9065DC), UINT32_C(0x3F1A6A3D), UINT32_C(0x4068BAFC), UINT32_C(0x42855556), UINT32_C(0xBF8F02BE), UINT32_C(0xBF0605F7), UINT32_C(0xBF39E838),
    UINT32_C(0x4032A9F5), UINT32_C(0x42855556), UINT32_C(0xBF5D8B31), UINT32_C(0xBF278F21), UINT32_C(0xBFC6C8CF), UINT32_C(0x3F99D5A3), UINT32_C(0x42855556), UINT32_C(0xBF150E3E),
    UINT32_C(0xBF2CCC9A), UINT32_C(0xBFD1A5BA), UINT32_C(0xBADE70B6), UINT32_C(0x42855556), UINT32_C(0xBE9DE2FD), UINT32_C(0xBF241AA5), UINT32_C(0xBFBBEFCC), UINT32_C(0x3DB96CAD),
    UINT32_C(0x42855556), UINT32_C(0xBD70E801), UINT32_C(0xBF0823CB), UINT32_C(0xBFAB95F1), UINT32_C(0x3E16FF4E), UINT32_C(0x42855556), UINT32_C(0x3DE9CB58), UINT32_C(0xBEA6A350),
    UINT32_C(0xBF823DCF), UINT32_C(0xBD028EB2), UINT32_C(0x42855556), UINT32_C(0x3E271E41), UINT32_C(0xBD828776), UINT32_C(0xBE43E81C), UINT32_C(0xBE3AF552), UINT32_C(0x42855556),
    UINT32_C(0x3DAF5F1D), UINT32_C(0x3E3E5CC8), UINT32_C(0x3F10D131), UINT32_C(0xBC0CB667), UINT32_C(0x42855556), UINT32_C(0xBDC504B7), UINT32_C(0x3EB96924), UINT32_C(0x3F676EDE),
    UINT32_C(0x3E40A412), UINT32_C(0x42855556), UINT32_C(0xBEA4ADC2), UINT32_C(0x3EE97131), UINT32_C(0x3FB76856), UINT32_C(0x3F9AE1D2), UINT32_C(0x42855556), UINT32_C(0xBF0B7DA4),
    UINT32_C(0x3F070DED), UINT32_C(0x3FF19131), UINT32_C(0x4012BC2E), UINT32_C(0x42855556), UINT32_C(0xBF43E677), UINT32_C(0x3F129B88), UINT32_C(0x400D5AA2), UINT32_C(0x405655EF),
    UINT32_C(0x42855556), UINT32_C(0xBF7496AA), UINT32_C(0x3EF0987C), UINT32_C(0x402073BA), UINT32_C(0x404815DE), UINT32_C(0x42855556), UINT32_C(0xBF929C5F), UINT32_C(0x3E849472),
    UINT32_C(0x40102A5F), UINT32_C(0x4056C0D9), UINT32_C(0x42855556), UINT32_C(0xBFA20782), UINT32_C(0x3AF4C821), UINT32_C(0x3FC4346E), UINT32_C(0x406917B9), UINT32_C(0x42855556),
    UINT32_C(0xBF1EDDB7), UINT32_C(0xBE1421EB), UINT32_C(0xBE464BEE), UINT32_C(0x3F9276BE), UINT32_C(0x41055555),
};

// Source: artifacts/round-material/drift-repair-v14-relaxed-wide-forward.json
// SHA256: 47209787ccc56322e978e428fc7b589038e4ea31152204fc1029e4771ae10d0f
// Attempted frame 3461; state has 93 exact float values.
static const double dango_material_contact_coupled3461_step = 0.0010416625300422311;
static const uint64_t dango_material_contact_coupled3461_step_bits = UINT64_C(0x3F51110CA0000000);
static const dango_material_ground_receipt dango_material_contact_coupled3461_receipts[5] = {
    {6, 0.0, 0.0, 0.72000002861022949, 0.0},
    {7, 0.0029061273671686649, 0.0020532049238681793, 0.72000002861022949, 0.005802573636174202},
    {8, 0.001996044535189867, 0.0014371521538123488, 0.72000002861022949, 0.0036647338420152664},
    {9, 0.0013377268332988024, 0.00096316332928836346, 0.72000002861022949, 0.0013377268332988024},
    {10, 0.0, 0.0, 0.72000002861022949, 0.0},
};
static const uint64_t dango_material_contact_coupled3461_receipt_bits[5][4] = {
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x0000000000000000)},
    {UINT64_C(0x3F67CE9740000000), UINT64_C(0x3F60D1E200000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x3F77C47080000000)},
    {UINT64_C(0x3F605A0240000000), UINT64_C(0x3F578BDA60000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x3F6E058100000000)},
    {UINT64_C(0x3F55EAD540000000), UINT64_C(0x3F4F8F9980000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x3F55EAD540000000)},
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x3FE70A3D80000000), UINT64_C(0x0000000000000000)},
};

static inline dango_constraint_state dango_material_contact_coupled3461_state(void)
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
    state.positions[0] = (b2Vec2){-0.597468913f, 0.186410785f};
    state.velocities[0] = (b2Vec2){-0.58131659f, 0.162699804f};
    state.inverse_mass[0] = 66.6666718f;
    state.positions[1] = (b2Vec2){-0.881069899f, 0.173474535f};
    state.velocities[1] = (b2Vec2){-0.600979447f, -2.53718829f};
    state.inverse_mass[1] = 66.6666718f;
    state.positions[2] = (b2Vec2){-1.15659571f, 0.105047688f};
    state.velocities[2] = (b2Vec2){-0.165145159f, -4.29225636f};
    state.inverse_mass[2] = 66.6666718f;
    state.positions[3] = (b2Vec2){-1.37116838f, -0.0808426961f};
    state.velocities[3] = (b2Vec2){-0.305685282f, -4.22150183f};
    state.inverse_mass[3] = 66.6666718f;
    state.positions[4] = (b2Vec2){-1.43584239f, -0.35727334f};
    state.velocities[4] = (b2Vec2){-1.85642779f, -3.85901928f};
    state.inverse_mass[4] = 66.6666718f;
    state.positions[5] = (b2Vec2){-1.31383979f, -0.613575578f};
    state.velocities[5] = (b2Vec2){-0.716454983f, -3.26388025f};
    state.inverse_mass[5] = 66.6666718f;
    state.positions[6] = (b2Vec2){-1.04194117f, -0.674432278f};
    state.velocities[6] = (b2Vec2){-0.0413320549f, -0.013179563f};
    state.inverse_mass[6] = 66.6666718f;
    state.positions[7] = (b2Vec2){-0.758572459f, -0.674999356f};
    state.velocities[7] = (b2Vec2){-0.0083900122f, -0.022466654f};
    state.inverse_mass[7] = 66.6666718f;
    state.positions[8] = (b2Vec2){-0.4766801f, -0.675000012f};
    state.velocities[8] = (b2Vec2){-0.524005294f, -0.000811390288f};
    state.inverse_mass[8] = 66.6666718f;
    state.positions[9] = (b2Vec2){-0.225207835f, -0.674499393f};
    state.velocities[9] = (b2Vec2){-0.858082712f, -0.218205869f};
    state.inverse_mass[9] = 66.6666718f;
    state.positions[10] = (b2Vec2){0.00655491278f, -0.670744658f};
    state.velocities[10] = (b2Vec2){-1.07650983f, 0.0086159762f};
    state.inverse_mass[10] = 66.6666718f;
    state.positions[11] = (b2Vec2){0.165250689f, -0.519653678f};
    state.velocities[11] = (b2Vec2){-0.900787294f, -0.447501361f};
    state.inverse_mass[11] = 66.6666718f;
    state.positions[12] = (b2Vec2){0.207883134f, -0.286746472f};
    state.velocities[12] = (b2Vec2){-0.719418466f, -0.713373661f};
    state.inverse_mass[12] = 66.6666718f;
    state.positions[13] = (b2Vec2){0.111018963f, -0.0704671964f};
    state.velocities[13] = (b2Vec2){-0.31349358f, -0.63212502f};
    state.inverse_mass[13] = 66.6666718f;
    state.positions[14] = (b2Vec2){-0.103424571f, 0.0268013906f};
    state.velocities[14] = (b2Vec2){-0.524722993f, -0.755224526f};
    state.inverse_mass[14] = 66.6666718f;
    state.positions[15] = (b2Vec2){-0.327131242f, 0.0997265428f};
    state.velocities[15] = (b2Vec2){-0.675351858f, -0.178006902f};
    state.inverse_mass[15] = 66.6666718f;
    state.positions[16] = (b2Vec2){-0.620968878f, -0.254963994f};
    state.velocities[16] = (b2Vec2){-0.683735371f, 0.198480532f};
    state.inverse_mass[16] = 8.33333302f;
    return state;
}
static const uint32_t dango_material_contact_coupled3461_float_bits[93] = {
    UINT32_C(0x3F2E147B), UINT32_C(0x3E87D865), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D3851EC), UINT32_C(0xBF3851EC),
    UINT32_C(0xBF18F3B9), UINT32_C(0x3E3EE278), UINT32_C(0xBF14D12A), UINT32_C(0x3E269AC7), UINT32_C(0x42855556), UINT32_C(0xBF618DCC), UINT32_C(0x3E31A34F), UINT32_C(0xBF19D9CA),
    UINT32_C(0xC022614B), UINT32_C(0x42855556), UINT32_C(0xBF940B54), UINT32_C(0x3DD7233E), UINT32_C(0xBE291BD0), UINT32_C(0xC0895A2A), UINT32_C(0x42855556), UINT32_C(0xBFAF8272),
    UINT32_C(0xBDA590DB), UINT32_C(0xBE9C82C8), UINT32_C(0xC087168B), UINT32_C(0x42855556), UINT32_C(0xBFB7C9AF), UINT32_C(0xBEB6EC88), UINT32_C(0xBFED9F6D), UINT32_C(0xC076FA2C),
    UINT32_C(0x42855556), UINT32_C(0xBFA82BE7), UINT32_C(0xBF1D134A), UINT32_C(0xBF376998), UINT32_C(0xC050E36A), UINT32_C(0x42855556), UINT32_C(0xBF855E54), UINT32_C(0xBF2CA798),
    UINT32_C(0xBD294BCD), UINT32_C(0xBC57EF18), UINT32_C(0x42855556), UINT32_C(0xBF4231CE), UINT32_C(0xBF2CCCC2), UINT32_C(0xBC097643), UINT32_C(0xBCB80BFD), UINT32_C(0x42855556),
    UINT32_C(0xBEF40F6A), UINT32_C(0xBF2CCCCD), UINT32_C(0xBF062536), UINT32_C(0xBA54B37B), UINT32_C(0x42855556), UINT32_C(0xBE669CE2), UINT32_C(0xBF2CABFE), UINT32_C(0xBF5BAB4F),
    UINT32_C(0xBE5F715C), UINT32_C(0x42855556), UINT32_C(0x3BD6CA98), UINT32_C(0xBF2BB5EC), UINT32_C(0xBF89CB13), UINT32_C(0x3C0D2A06), UINT32_C(0x42855556), UINT32_C(0x3E29377A),
    UINT32_C(0xBF050806), UINT32_C(0xBF6699FF), UINT32_C(0xBEE51EE6), UINT32_C(0x42855556), UINT32_C(0x3E54DF51), UINT32_C(0xBE92D06F), UINT32_C(0xBF382BCF), UINT32_C(0xBF369FA8),
    UINT32_C(0x42855556), UINT32_C(0x3DE35DE9), UINT32_C(0xBD90511B), UINT32_C(0xBEA0823B), UINT32_C(0xBF21D2F2), UINT32_C(0x42855556), UINT32_C(0xBDD3D043), UINT32_C(0x3CDB8E97),
    UINT32_C(0xBF06543F), UINT32_C(0xBF415665), UINT32_C(0x42855556), UINT32_C(0xBEA77DBF), UINT32_C(0x3DCC3D6E), UINT32_C(0xBF2CE3DC), UINT32_C(0xBE364771), UINT32_C(0x42855556),
    UINT32_C(0xBF1EF7D1), UINT32_C(0xBE828AA4), UINT32_C(0xBF2F0948), UINT32_C(0x3E4B3E7B), UINT32_C(0x41055555),
};

// Source: artifacts/round-material/drift-repair-v16-wide-forward-80-100.json
// SHA256: df6a76c17c580e53f00a8610046271313a666ec234079ca7a1006d029c90b902
// Attempted frame 1687; state has 93 exact float values.
static const double dango_material_contact_soft1687_step = 0.0010416625300422311;
static const uint64_t dango_material_contact_soft1687_step_bits = UINT64_C(0x3F51110CA0000000);
static const dango_material_ground_receipt dango_material_contact_soft1687_receipts[3] = {
    {3, 0.0, 0.0, 3.3941125869750977, 0.0},
    {4, 0.0029437250923365355, 0.0010864062933251262, 3.3941125869750977, 0.0058878893032670021},
    {5, 0.0, 0.0, 3.3941125869750977, 5.9694262745324522e-05},
};
static const uint64_t dango_material_contact_soft1687_receipt_bits[3][4] = {
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x400B272480000000), UINT64_C(0x0000000000000000)},
    {UINT64_C(0x3F681D7060000000), UINT64_C(0x3F51CCB7E0000000), UINT64_C(0x400B272480000000), UINT64_C(0x3F781DE640000000)},
    {UINT64_C(0x0000000000000000), UINT64_C(0x0000000000000000), UINT64_C(0x400B272480000000), UINT64_C(0x3F0F4C0740000000)},
};

static inline dango_constraint_state dango_material_contact_soft1687_state(void)
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
    state.positions[0] = (b2Vec2){-1.54157555f, -0.448325276f};
    state.velocities[0] = (b2Vec2){0.0302618295f, -0.0491277874f};
    state.inverse_mass[0] = 104.166664f;
    state.positions[1] = (b2Vec2){-1.36193752f, -0.58729291f};
    state.velocities[1] = (b2Vec2){0.00668760017f, -0.120731339f};
    state.inverse_mass[1] = 104.166664f;
    state.positions[2] = (b2Vec2){-1.14732099f, -0.661610842f};
    state.velocities[2] = (b2Vec2){-0.0850031674f, -0.385538399f};
    state.inverse_mass[2] = 104.166664f;
    state.positions[3] = (b2Vec2){-0.921171188f, -0.682560325f};
    state.velocities[3] = (b2Vec2){-0.0455636084f, -0.236296773f};
    state.inverse_mass[3] = 104.166664f;
    state.positions[4] = (b2Vec2){-0.694058359f, -0.683999836f};
    state.velocities[4] = (b2Vec2){-0.0364581868f, 1.13577698e-08f};
    state.inverse_mass[4] = 104.166664f;
    state.positions[5] = (b2Vec2){-0.506245673f, -0.684000015f};
    state.velocities[5] = (b2Vec2){-0.0828271061f, 0.0036330428f};
    state.inverse_mass[5] = 104.166664f;
    state.positions[6] = (b2Vec2){-0.383838415f, -0.588997364f};
    state.velocities[6] = (b2Vec2){0.500687957f, -0.783443809f};
    state.inverse_mass[6] = 104.166664f;
    state.positions[7] = (b2Vec2){-0.340970576f, -0.439049721f};
    state.velocities[7] = (b2Vec2){0.999674261f, -1.2857374f};
    state.inverse_mass[7] = 104.166664f;
    state.positions[8] = (b2Vec2){-0.39694488f, -0.25997287f};
    state.velocities[8] = (b2Vec2){-0.431381553f, -2.20030189f};
    state.inverse_mass[8] = 104.166664f;
    state.positions[9] = (b2Vec2){-0.529762864f, -0.0901802182f};
    state.velocities[9] = (b2Vec2){-0.168798298f, -2.58161283f};
    state.inverse_mass[9] = 104.166664f;
    state.positions[10] = (b2Vec2){-0.711276293f, 0.0425757021f};
    state.velocities[10] = (b2Vec2){0.295401454f, -2.30312729f};
    state.inverse_mass[10] = 104.166664f;
    state.positions[11] = (b2Vec2){-0.925275266f, 0.118624642f};
    state.velocities[11] = (b2Vec2){0.583170712f, -1.51953471f};
    state.inverse_mass[11] = 104.166664f;
    state.positions[12] = (b2Vec2){-1.15152562f, 0.138439745f};
    state.velocities[12] = (b2Vec2){0.617876351f, -1.1227541f};
    state.inverse_mass[12] = 104.166664f;
    state.positions[13] = (b2Vec2){-1.37584686f, 0.102915116f};
    state.velocities[13] = (b2Vec2){0.51897496f, -0.553590119f};
    state.inverse_mass[13] = 104.166664f;
    state.positions[14] = (b2Vec2){-1.5655731f, -0.0219291002f};
    state.velocities[14] = (b2Vec2){0.239194453f, -0.140979573f};
    state.inverse_mass[14] = 104.166664f;
    state.positions[15] = (b2Vec2){-1.63081849f, -0.239472628f};
    state.velocities[15] = (b2Vec2){-0.020251222f, -0.0631534234f};
    state.inverse_mass[15] = 104.166664f;
    state.positions[16] = (b2Vec2){-0.972528875f, -0.282542199f};
    state.velocities[16] = (b2Vec2){-0.204275459f, -2.13751388f};
    state.inverse_mass[16] = 13.0208311f;
    return state;
}
static const uint32_t dango_material_contact_soft1687_float_bits[93] = {
    UINT32_C(0x3F0B4396), UINT32_C(0x3E595A3C), UINT32_C(0x3F266666), UINT32_C(0x3F9C28F6), UINT32_C(0x3F3AE148), UINT32_C(0x3F88F5C3), UINT32_C(0x3D1374BC), UINT32_C(0xBF3851EC),
    UINT32_C(0xBFC55259), UINT32_C(0xBEE58AE4), UINT32_C(0x3CF7E7A8), UINT32_C(0xBD493A38), UINT32_C(0x42D05555), UINT32_C(0xBFAE53F8), UINT32_C(0xBF1658D4), UINT32_C(0x3BDB23A8),
    UINT32_C(0xBDF741FE), UINT32_C(0x42D05555), UINT32_C(0xBF92DB6A), UINT32_C(0xBF295F54), UINT32_C(0xBDAE1624), UINT32_C(0xBEC5654A), UINT32_C(0x42D05555), UINT32_C(0xBF6BD1E0),
    UINT32_C(0xBF2EBC46), UINT32_C(0xBD3AA0E8), UINT32_C(0xBE71F7C8), UINT32_C(0x42D05555), UINT32_C(0xBF31ADCF), UINT32_C(0xBF2F1A9D), UINT32_C(0xBD15552E), UINT32_C(0x32432000),
    UINT32_C(0x42D05555), UINT32_C(0xBF019951), UINT32_C(0xBF2F1AA0), UINT32_C(0xBDA9A142), UINT32_C(0x3B6E1858), UINT32_C(0x42D05555), UINT32_C(0xBEC48678), UINT32_C(0xBF16C888),
    UINT32_C(0x3F002D16), UINT32_C(0xBF488FC6), UINT32_C(0x42D05555), UINT32_C(0xBEAE93B2), UINT32_C(0xBEE0CB20), UINT32_C(0x3F7FEAA7), UINT32_C(0xBFA4930B), UINT32_C(0x42D05555),
    UINT32_C(0xBECB3C5C), UINT32_C(0xBE851B2A), UINT32_C(0xBEDCDE0B), UINT32_C(0xC00CD1BF), UINT32_C(0x42D05555), UINT32_C(0xBF079E8A), UINT32_C(0xBDB8B068), UINT32_C(0xBE2CD976),
    UINT32_C(0xC0253925), UINT32_C(0x42D05555), UINT32_C(0xBF361634), UINT32_C(0x3D2E63DC), UINT32_C(0x3E973EDC), UINT32_C(0xC0136670), UINT32_C(0x42D05555), UINT32_C(0xBF6CDED7),
    UINT32_C(0x3DF2F17A), UINT32_C(0x3F154AAD), UINT32_C(0xBFC2801D), UINT32_C(0x42D05555), UINT32_C(0xBF936531), UINT32_C(0x3E0DC326), UINT32_C(0x3F1E2D25), UINT32_C(0xBF8FB668),
    UINT32_C(0x42D05555), UINT32_C(0xBFB01BC0), UINT32_C(0x3DD2C529), UINT32_C(0x3F04DB8B), UINT32_C(0xBF0DB815), UINT32_C(0x42D05555), UINT32_C(0xBFC864B3), UINT32_C(0xBCB3A4A8),
    UINT32_C(0x3E74EF64), UINT32_C(0xBE105CF3), UINT32_C(0x42D05555), UINT32_C(0xBFD0BEA9), UINT32_C(0xBE753850), UINT32_C(0xBCA5E5E4), UINT32_C(0xBD815695), UINT32_C(0x42D05555),
    UINT32_C(0xBF78F7A7), UINT32_C(0xBE90A95F), UINT32_C(0xBE512D96), UINT32_C(0xC008CD07), UINT32_C(0x41505553),
};

#endif

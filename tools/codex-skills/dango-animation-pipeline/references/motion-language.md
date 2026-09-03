# Dango motion language

Use these terms to make animation choices reviewable. They describe Phase 1 intent, not fixed numerical values.

## Character qualities

- **Weight:** motion anticipates force, contact compresses the body, and recovery settles rather than stopping instantly.
- **Breathing:** small asymmetric volume change around a stable ground contact; it must not look like the whole image is uniformly zooming.
- **Softness:** outline changes follow force with limited squash, stretch, and delayed secondary recovery.
- **Quietness:** long calm intervals, low amplitude, and no perpetual attention-seeking bounce, sparkle, or screen crossing.
- **Readability:** start, contact, transfer, and settle poses remain distinguishable at desktop-pet scale.
- **Spatial contact:** grabbing the island and landing on the ground use stable semantic anchors; the body must appear to touch rather than float beside the surface.
- **Flat modularity:** customizable body fill, expression parts, and hat remain independent solid-color layers through every pose. Eyes belong to expression animation, not face customization. Motion cannot rely on highlights, gradients, lighting, or baked combinations.
- **Limbless silhouette:** the Dango has no legs, feet, paws, or arms. Keep one continuous rounded base; express locomotion and climbing through center-of-mass shift, squash/stretch, and contact deformation rather than limb-like bumps.
- **Authored semantics, physical feel:** authored poses and events decide what the action means. The approved Box2D presentation rig may simulate mass, collision, spring joints, contact compression, decay, and secondary lag, but never decides Core position, collision, permissions, or the interaction result.

## Timing pattern

Describe each clip with:

1. intent and start state;
2. anticipation;
3. primary action;
4. contact or state-transfer event;
5. overshoot or compression when appropriate;
6. settle and final state;
7. legal interruption points;
8. reduced-motion variant;
9. deterministic variant inputs, if any.

Do not choose timing only because it looks smooth in a large preview. The pose sequence must remain legible at the target canvas and must map to the Core state transition.

## Representative samples

### Idle

Keep the ground contact stable. Separate breathing from blinking. Breathing may combine slight upper-body contour change with small opposing face motion; avoid equal X/Y scaling of the entire character. Reduced motion removes secondary sway and lowers update frequency while keeping the character alive.

### Landing

Show fall anticipation, first contact, short compression, smaller rebound, and settle. The `landed` event is tied to first ground contact rather than the final resting frame. Reduced motion keeps contact and final pose but reduces overshoot.

### ClimbUp

Show approach, reach, `island-grabbed` contact, weight transfer, body lift, edge crossing, and stable island stance. The handless round body still needs an unambiguous contact patch or deformation. Reduced motion may shorten secondary deformation but cannot remove the semantic grab and transfer.

## Box2D visual soft-body motion

- Express each contact in one interaction coordinate system with a semantic anchor, contact normal, strength, shared start time, parameter version, and deterministic seed.
- Build each Dango from a kinematic Core anchor and an ordered ring of Box2D dynamic nodes with perimeter and radial spring joints. Treat node count, mass, gravity, joint frequency, damping, substeps, and deformation limits as Spike candidates until measured on the real desktop host.
- Keep authored poses as the fallback and the source of semantic events. Never integrate visual spring state back into Core position or collision.
- Make eyes follow the locally deformed body while expression remains an independent track. Drive the hat from a dynamic attachment point; a small capped lag is allowed, drift and body penetration are not.
- For two-Dango contact, use one interaction director to emit a paired constraint and complementary deformation. Do not let both characters independently solve the same contact.
- Sample simulation on a fixed step with bounded catch-up. The same Windows build, intent, participants, authoritative time, contact data, parameter version, and seed must reproduce within the approved tolerance; cross-device vertex identity is not a shared semantic requirement.
- Reduced motion defaults to authored key poses without continuous soft-body stepping. It must keep contact events and the final pose.
- If native loading, simulation validity, renderer consumption, or the performance budget fails, destroy the affected physics instance and use authored key poses. Do not switch engines or renderers without an approved design change.

## Avoid

- continuous metronomic bobbing;
- identical squash for breathing, clicking, landing, and climbing;
- motion that moves the ground contact without a state reason;
- high-frequency facial changes or surprise reactions during idle;
- effects that dominate the desktop or make transparency edges obvious;
- shading, highlights, gradients, glow, or texture used to fake volume;
- expression or hat variants baked into every body color;
- feet, legs, paws, arms, or paired bottom protrusions that read as feet;
- animations whose visual result depends on private desktop content.

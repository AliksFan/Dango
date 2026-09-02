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
- **Authored semantics, simulated feel:** authored poses and events decide what the action means. A bounded spring-damper layer may add contact compression, decay, and secondary lag, but never decide position, collision, permissions, or the interaction result.

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

## Lightweight secondary motion

- Express each contact in one interaction coordinate system with a semantic anchor, contact normal, strength, shared start time, parameter version, and deterministic seed.
- Combine axial compression along the normal with limited transverse compensation, a hard deformation cap, and damped recovery. Treat all numeric values as Spike candidates until measured on the real desktop host.
- Keep authored poses as the fallback and the source of semantic events. Never integrate visual spring state back into Core position or collision.
- Make eyes follow the locally deformed body while expression remains an independent track. Drive the hat from a dynamic attachment point; a small capped lag is allowed, drift and body penetration are not.
- For two-Dango contact, use one interaction director to emit a paired constraint and complementary deformation. Do not let both characters independently solve the same contact.
- Sample simulation on a fixed step. The same intent, participants, authoritative time, contact data, parameter version, and seed must reproduce the same result; render interpolation may not change it.
- Reduced motion removes delayed oscillation and nonessential secondary sway, lowers deformation, and may lower the target refresh rate while keeping contact events and the final pose.
- If the renderer or performance budget cannot support this layer, disable it and use the authored key poses. Never substitute a full physics engine without a new approved ADR.

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

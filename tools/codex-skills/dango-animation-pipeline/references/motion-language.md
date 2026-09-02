# Dango motion language

Use these terms to make animation choices reviewable. They describe Phase 1 intent, not fixed numerical values.

## Character qualities

- **Weight:** motion anticipates force, contact compresses the body, and recovery settles rather than stopping instantly.
- **Breathing:** small asymmetric volume change around a stable ground contact; it must not look like the whole image is uniformly zooming.
- **Softness:** outline changes follow force with limited squash, stretch, and delayed secondary recovery.
- **Quietness:** long calm intervals, low amplitude, and no perpetual attention-seeking bounce, sparkle, or screen crossing.
- **Readability:** start, contact, transfer, and settle poses remain distinguishable at desktop-pet scale.
- **Spatial contact:** grabbing the island and landing on the ground use stable semantic anchors; the body must appear to touch rather than float beside the surface.

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

## Avoid

- continuous metronomic bobbing;
- identical squash for breathing, clicking, landing, and climbing;
- motion that moves the ground contact without a state reason;
- high-frequency facial changes or surprise reactions during idle;
- effects that dominate the desktop or make transparency edges obvious;
- animations whose visual result depends on private desktop content.

# Dango Phase 1 asset contract

Read the live repository files before relying on this snapshot:

- `src/Dango.Core/Assets/AssetPackage.cs`
- `src/Dango.Core/Assets/AssetPackageParser.cs`
- `src/Dango.Core/Animation/DangoActionState.cs`
- `assets/placeholder/phase-1.asset.json`
- `assets/placeholder/phase-1.asset.schema.json`

This reference records the AP-000 baseline at animation branch commit `a5792e7`. The Phase 1 branch had advanced to `70d42dd`, with no changes under the Core asset/animation model, placeholder package, or Core asset tests relative to the animation branch base.

## Runtime concepts

The current runtime contract version is `1` and includes:

- `assetVersion` and runtime compatibility bounds;
- one logical `canvas` with width, height, pixel density, and origin;
- ordered `layers` whose sources are package-relative paths and whose slots must exist;
- named `slots` for body, face, shadow, and accessory placement;
- `clips` with duration, loop behavior, frames, and transform tracks;
- `actions` mapping `DangoActionState` names to clip IDs;
- named `anchors` used by interaction events;
- simplified click, drag, and collision `hitShapes`;
- timed `events` referencing clips and optional anchors;
- acyclic `fallbacks` between clip IDs.

The Core parser, not JSON Schema alone, is the semantic runtime authority. It rejects unsupported versions, missing references, paths outside the package, invalid keyframes, out-of-canvas positions, missing required hit-shape purposes, invalid events, and fallback cycles.

## Current action and clip keys

| State | Clip key | Required in first candidate |
|---|---|---|
| `Idle` | `idle` | yes |
| `Wander` | `wander` | yes |
| `ApproachIsland` | `approach-island` | yes |
| `ClimbUp` | `climb-up` | yes |
| `OnIsland` | `on-island` | yes |
| `ClimbDown` | `climb-down` | yes |
| `Dragged` | `dragged` | yes |
| `Falling` | `falling` | yes |
| `Landing` | `landing` | yes |
| `Reacting` | `reacting` | yes |
| `Hidden` | none | runtime-hidden state; do not create a fake visible clip |

The current sample canvas is 160 by 120 logical units with origin `(0, 0)` and pixel density `1`. Treat these values as a prototype baseline, not a permanent art resolution. Do not change them during preproduction without an approved contract change.

## Current semantic points

- Slots: `body`, `face`, `shadow`, `accessory`.
- Anchors: `ground-contact`, `click`, `island-grab`, `island-stand`.
- Required hit-shape purposes: click, drag, collision.
- Current timed events: `island-grabbed` on `climb-up`; `landed` on `landing`.
- Current fallback: `reacting` to `idle`.

## Known preproduction pressure points

The current Schema stores slots and anchors once per package, not per frame. It also has one frame layer list rather than independent facial sub-clips. If selected art proves that outline frames need per-frame accessory anchors, changing occlusion, or independent facial timing, record the gap for P1-004. Do not encode new meanings into unrelated fields.

The approved preproduction input also requires a simple first-release customization model with replaceable solid body color and hat categories. Eyes belong to expression animation and are not a customization preset. Contract version 1 has no explicit tint parameter, variant set, or per-frame hat anchor. During preproduction, keep body, expression, and hat parts visually separate and document the missing runtime representation; do not silently implement UI, persistence, networking, or overload existing fields.

Candidate assets stay under `assets/preproduction/phase-1-dango/` and are not loaded by the app by default. Moving selected exports into `assets/placeholder/` is a separate integration action.

# Dango animation asset review checklist

Run the sections that match the artifact status. A draft may fail candidate checks, but the failure must remain visible.

## Source and provenance

- Every selected file has a package-relative path and a provenance entry.
- Source type is project-created, AI draft with human review, or project-code generated.
- Tool, date, prompt or creation note, modifications, and license are recorded.
- No third-party character, existing IP, sensitive desktop content, watermark, or unknown source remains.
- Discarded generations, caches, backups, and temporary frames are not committed.

## Raster structure

- PNG files decode, use the same canvas dimensions, and contain Alpha.
- Transparent background is real, not a checkerboard baked into pixels.
- No obvious white/black fringe, dirty transparent pixels, cropped outline, or atlas bleed.
- Body, face, blush, shadow, and accessory boundaries match the approved layer plan.
- Small-size preview remains readable without fine texture noise.

## Continuity and contact

- Body identity, proportions, palette, light direction, and line weight remain consistent.
- Ground contact does not drift during idle.
- Accessory attachment does not jump between outline frames.
- Landing first contact aligns with `ground-contact`.
- Climb contact aligns with `island-grab`; final stance aligns with `island-stand`.
- Occlusion order does not visibly flip without an explicit layer change.

## Runtime mapping

- Every required action maps to a valid clip; `Hidden` has no fake visible clip.
- Frame times are inside clip duration and ordered intentionally.
- Transform targets and frame layer references exist.
- Events reference valid clips and anchors at meaningful times.
- Fallbacks are safe, semantically weaker, and acyclic.
- Candidate files remain editor-neutral and package-relative.

## Evidence labels

Report each independently:

- skill and script validation;
- asset structural validation;
- static visual review;
- runtime parsing;
- WPF playback;
- real Windows desktop behavior;
- human experience acceptance;
- commit, installation, merge, push, deployment, and external-resource state.

Never collapse the first four into proof of the last three.

---
name: dango-animation-pipeline
description: Design, generate, organize, or review Dango character animation assets against the project's versioned asset contract. Use only for Dango motion specifications, raster drafts, export preparation, provenance, or animation-asset acceptance; do not route generic image requests or WPF rendering implementation here.
---

# Dango Animation Pipeline

Prepare Dango animation assets without confusing visual drafts, runtime structure, desktop playback, or human acceptance.

## Start safely

1. Locate the Dango repository root and read its current `AGENTS.md`.
2. Check the current branch, HEAD, worktree status, approved animation design/plan, and task authorization.
3. Read [asset-contract.md](references/asset-contract.md) before creating or validating a runtime candidate.
4. Classify every artifact as `draft`, `candidate`, or `accepted`. Never silently promote it.
5. Preserve unrelated changes and stop if the live asset contract differs from the reference.

## Choose one mode

- **Motion design:** Read [motion-language.md](references/motion-language.md). Produce intent, key poses, timing, interruption points, events, anchors, reduced-motion behavior, and open visual questions.
- **Raster draft generation:** Read [motion-language.md](references/motion-language.md). Use the available `imagegen` skill, keep prompts and provenance, and label outputs `draft`. Do not infer continuity or transparent-layer correctness from one generated image.
- **Export preparation:** Read [asset-contract.md](references/asset-contract.md) and [review-checklist.md](references/review-checklist.md). Organize only selected project-owned files and run `scripts/validate_export.py` before proposing a runtime candidate.
- **Asset review:** Read [review-checklist.md](references/review-checklist.md), run deterministic checks, and report asset structure separately from visual review, runtime playback, desktop behavior, and human acceptance.

## Project invariants

- The runtime truth is a versioned, editor-neutral asset package; WPF controls, Storyboards, Godot NodePaths, Figma nodes, or proprietary editor objects cannot be the only source of truth.
- Use authored key poses for semantic timing and fallback. The approved P1-004 runtime direction may use a Box2D soft-body presentation rig for continuous deformation, but physics output never decides Core state, position, collision, permissions, or interaction results. Do not make every motion an overall sine-wave scale.
- Shared semantics use stable action/clip IDs, events, anchors, timing, and fallbacks. Local breathing or blinking cannot change shared pose, occupancy, or interaction results.
- Do not add sleep, cuddling, group, couple, multiplayer, or formal content unless the current approved scope explicitly includes it.
- Do not buy, install, subscribe to, upload to, or bind the project to an external tool without separate authorization.
- Never use third-party character art or an existing IP as a deliverable source. Record the origin and license of every selected file.
- Image generation, schema parsing, or a static pose sheet is not proof of WPF playback or real desktop acceptance.

## Output contract

For each task, return:

- mode and authorized action/clip scope;
- artifact status and paths;
- prompt/source/provenance changes;
- deterministic checks with actual results;
- visual questions requiring user review;
- unverified runtime, desktop, and human-acceptance items;
- commit, installation, merge, push, deployment, and external-resource status.

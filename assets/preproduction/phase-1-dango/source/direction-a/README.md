# Direction A deterministic source

> Artifact status: candidate for AP-003 visual review; not accepted runtime art.

This folder translates the user-selected outline-free rounded direction A into editor-neutral SVG layers on the Phase 1 `160 x 120` logical canvas.

- `body.svg`: replaceable solid body color; one continuous rounded base and no limbs.
- `eyes-default.svg`: the only default expression layer. Eyes are animation content, not customization presets.
- `hat-sprout.svg`: removable solid-color hat layer.
- `preview-default.svg`: review-only composition of the five layer responsibilities.
- `export-svg.ps1`: reproducible local export to transparent PNG with Microsoft Edge.

There are deliberately no mouth, blush, or shadow layers. The SVG files contain no gradient, filter, lighting, texture, or opacity effect. Running the export script writes PNG files under `../../export/direction-a/`; validate that directory with the export provenance before proposing it as a runtime candidate.

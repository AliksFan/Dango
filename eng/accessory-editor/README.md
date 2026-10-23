# Local accessory image editor — E0 dependency evidence

This is a curated, maintainable miniPaint source subset for dependency validation.
It is not the E2 unified editor or product/manual acceptance.

Upstream: miniPaint 4.14.3, commit `a79733eb803fc97084ef0ee4faa96b031e69e1c0`, MIT.
`upstream-sources.json` identifies each copied input and its pre-adaptation SHA256.
The audited maintained copies are under `vendor/`; Dango replacements are under
`overrides/`. `import-upstream.mjs <audited-checkout>` reproduces this curation
without copying the embedded upstream credentials or the full dependency graph.
It must only be run against the exact pinned commit. It never installs packages.

## Rebuild and checks

Requires existing Node/npm. From this directory:

```text
npm ci --ignore-scripts --no-audit --no-fund
npm run build
npm run verify
node budget-tests.mjs
node smoke.mjs
```

The fresh npm lock pins jQuery 3.7.1 (MIT, runtime), esbuild 0.25.11 (MIT,
build only), its npm platform package (MIT), and playwright-core 1.56.1
(Apache-2.0, background tests only). `npm ci --ignore-scripts` uses esbuild's
platform package without running installation scripts. No system component or
browser download is required. Smoke uses the existing Edge installation.
Installed package license files remain in their packages; the shipped set has
the full miniPaint and jQuery MIT texts and a NOTICE describing modifications.

`build.mjs` writes only the six resources under the desktop `Editing/Web`
directory plus `manifest.json`. The latter excludes itself and hashes every
shipped file; it must agree with the host's exact inventory verification.
`bundle-inputs.json` records the actual esbuild closure. jQuery slim deliberately
excludes Ajax. AlertifyJS is replaced by local Dango feedback; 1.14.0 was found
to contain a GPLv3 LICENSE and is not in the final lock, input graph, or output.

## API and privacy boundary

After `window` load, `window.DangoMiniPaint` is available with `apiVersion: 1`.
`dango-editor-ready` is a DOM event only; this code sends no native messages.
The native bridge must establish its own session/request/revision identities.

- `await load({width, height, pngBase64})`: native-normalized static PNG source.
- `exportProject()`: `{schemaVersion:1,width,height,layers,assets}`. Image assets
  are `{id:"asset-1",pngBase64}` and image layers use `assetId`. No source paths,
  external URLs, browser images, or arbitrary upstream project imports.
- `await load(exportProject())`: restores supported image, brush, and text
  layers, using the checked renderer registry. Decode/shape errors retain the
  current document. Project input is for native-vetted internal data only.
- `exportPng()`: `{width,height,pngBase64}` from all visible supported layers.
  Native must independently validate the output and all product budgets.

Retained tools: object select, brush, erase, magic erase, fill, system-font text,
crop, layer creation/duplicate/reorder/rename/raster/visibility and undo/redo.
Flip/rotate currently act on the selected layer. The bounded native wrapper,
true transparency restore, whole-output pixel/rig transforms, unified coordinate
undo, atomic revisions, lifecycle/focus/Chinese labels and shared host theme
mapping are E2 work. This E0 API does not claim those product contracts.

No external services, arbitrary URL/file import, browser download, printing,
cloud APIs, remote fonts, font files, image/icon assets, service worker,
IndexedDB, localStorage or sessionStorage are used. Undo storage is memory only.
The canvas renderer, text cursor and local numeric button repetition use the
upstream display/input schedulers. There is no body/accessory physics timer.
The CSP has default/connect/font/worker/object/frame sources set to none;
script self, image self/data/blob, and local/inline styles only. Native still
must enforce navigation, request, download, permission and origin boundaries.

## Budget status

Current defensive prototype caps: 4096 side, 16,000,000 canvas pixels, 16 layers,
64,000,000 decoded asset/rendered layer pixels, 64 MiB project JSON characters,
20 undo actions and 64 MiB conservatively accounted retained undo payload.
Image undo preflights encoded storage before changing pixels. Retained vector
history uses explicit payload accounting; direct brush/text growth is bounded
at the source (32768 brush points, 65536 text characters). Layer action guards
bound creation and resized geometry. Reduced-quota tests exercise rejection
before pixel mutation and release on undo/redo/free. This payload ceiling is
not a bound on total WebView heap, decoded canvases, GPU buffers, or process RSS.
Native transfer limits are independently enforced and may be stricter.
Maximum-size/history pressure and overall peak-memory/latency acceptance remain
unproven here; the parent E0 evidence record owns those measurements. This
dependency candidate cannot close the whole E0 gate or authorize E1/E2 work.

## Verification boundary

`verify.mjs` checks exact inventory/hash coverage, actual bundled npm inputs,
forbidden-source absence, MIT text presence, and local-only CSP/CSS resources.
`smoke.mjs` opens only background headless Edge: PNG/project roundtrip, failed
input preservation, unsupported media/URL/size and nine malformed layer shape
rejections, real brush input, editable reopen and undo/redo. A 16-layer quota
stroke must preserve the complete project and undo history. The strict internal
schema bounds geometry, typed opacity, exact renderers, brush data and text
parameters before document/history mutation. The smoke checks JS errors and
resource requests; static verification also rejects unresolved external snippet
credits in the actual bundle closure.
Neither check establishes foreground appearance, WPF/WebView2 integration,
true input focus ownership, all tool semantics, or human visual acceptance.

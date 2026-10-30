# Local accessory editor — miniPaint integration

## Current unified editor (2026-10-28)

The ordinary editor now uses miniPaint for images, layers and history, with
Dango's group skeleton commands in that same history. Image and skeleton tools
are 32 px icons on the left, with hover names and a one-line usage hint at the
lower left. A short palette and a larger object inspector float at the upper
and lower right and retract independently to reveal the canvas. The inspector
has layer-list, skeleton-parameter and layer-parameter icons in a vertical rail at its far right. Its layer list scrolls independently beneath fixed actions, and group headers expand or collapse their members. Folder rows show group names and member counts,
with nested, indented member layers and connecting lines. Ungrouped layers stay
at the list root, and selection shows the current layer's ownership. Group collapse is session-only and does not alter the project or image. Selecting a folder keeps the layer
view open; the skeleton icon or skeleton tools open its parameters.
The collapsed inspector keeps its three page icons; any icon expands that
page. Layer/group creation, duplicate, rasterize and both layer ordering actions
share one row. Layer and group trash icons align at the right edge of their own
rows; group deletion targets that row, preserves member layers and supports undo. Visibility
uses open/closed eye icons and matching action names. The palette header contains
the current swatch, editable hex value and all three mode icons; its picker fills
the body below, without a separate title or hex-label row.
The layer tab uses its whole page for the list. Skeleton and layer parameters
have their own right-hand pages, retaining the original wired controls. There
is no bottom parameter dock. Short windows switch between basic and fine
skeleton parameters within the same page. Layer/group names use double-click
or a right-click rename menu in the list, with one dialog and unified undo.
Both right panels share their width and expanded/collapsed alignment.
The native window has a compact icon
footer and an 800×640 DIP minimum. Wearing parameters remain in the outer draft.
The native caption strip is removed; the three window controls reuse the home
window's shared template, with a matching 132×38 DIP native browser cutout.
Missing image source dimensions in native projects are derived from decoded PNG
pixels before raster tools run, independently of the displayed layer size.
`canvas-view.js` keeps the existing Dango zoomView as the single view owner.
Image pixels, finite transparency checker and skeleton follow the same transform;
the viewport stays full-size, clips document content and clears on each render.
Panning/zooming leave exports, edit sequence and undo/redo unchanged.

Ctrl+C copies the current layer, explicitly selected group (including all members), or active magic-wand pixels. Native clipboard data contains PNG/Bitmap for other applications plus a validated editable fragment for Dango. Internal Ctrl+V remaps group/node/layer identities and preserves editable data in one undo action; external PNG/JPEG paste and file drop still add a single image layer.
The handler reads only image Files supplied by the paste event; it never polls the
clipboard. Input fields retain text paste. New accessories open a 512×512 blank
transparent project directly, and accessory selection remains in the library. The outer wearing page
shows only the selected named thumbnail to the left of its compact action buttons. The selected preview has no card border or background.

Original two-color cat ears, a cat tail and a ribbon live under
[`examples/warm-companions`](examples/warm-companions/README.md), with reproducible
layered PNG/SVG sources, native projects, skeleton parameters and a guarded
append command for the independent fictional test library.

The current browser API/project schema is 2; native projects are version 2,
bridge protocol 2, manifests schema 3, and skeleton definitions version 1.
Snapshot data is `{project,png}`. `project` owns name, skeleton, editable layers,
source assets and ordered rendering pieces. Each snapshot header carries its
captured `editSequence`; `Changed` notifications coalesce over 200 ms. A content
change cancels an old export and refreshes an already enabled native preview.
Neither sequence nor temporary input/view state is persisted.

```text
npm run build
npm run verify
npm run test:skeleton-input
npm run test:unified
npm run test:layout
npm run test:inspector
npm run test:view
npm run test:pages
npm run test:rename
npm run test:raster
node smoke.mjs
node web-geometry.test.mjs
node transport-tests.mjs
node budget-tests.mjs
```

See [ADR 0008](../../docs/adr/0008-accessory-shared-joints-and-unified-project.md)
for shared joints, versions, numerical mapping and defensive budgets. Automated
checks do not establish Windows appearance, gesture feel or maximum load.

## Historical S0 input fixture

From this directory, using the existing locked Node dependencies and Edge:

```text
npm run test:skeleton-input
npm run fixture:skeleton
```

The second command prints an ephemeral loopback URL. Opening it is a separate
foreground action, subject to the current screen-use authorization. Ctrl+C
stops that server. The specimen never opens a browser automatically. Tests use
headless Edge and close their browsers and ephemeral servers in `finally`.

`skeleton-commands.js` owns DOM-free geometry commands and in-memory history;
`skeleton-input.js` owns pointer/keyboard routing, selection and gesture
snapshots. The small `s0/` consumer uses two synthetic chains, one attached root,
overlapping P/R/J, fixed synthetic image layers and an ungrouped layer. New
groups are empty and root-only. No product entry, generated Web bundle, native
bridge, formal project schema, physics, skin binding or persistent store uses
this specimen as its product UI. The shared commands and input adapter are
consumed by the unified editor; the fixture's independent history is test-only.

Alt+left click cycles ordinary overlapping nodes. During attachment, pressing
Alt cycles the current candidates once; pointer movement and the final
release/click preserve that candidate identity. An Alt-modified click in the
sticky command previews without committing; an unmodified left click commits.
This is an S0 candidate-switching default awaiting interaction feedback.

The sample uses the semantic palette in `theme.css`, plus a minimal canvas and
group list. It does not claim the complete UI contract or product acceptance.
See [S0 evidence and pending manual checks](../../docs/verification/2026-10-26-accessory-skeleton-s0.md).

## Historical E0 dependency evidence

The following records the dependency candidate at E0. Later integration above
does not turn those historical checks into product/manual acceptance.

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

The Windows preview CI uses Node 24 and the locked restore above, runs `build`
and `verify`, then requires these generated files to match the checkout:

```text
git diff --exit-code -- bundle-inputs.json ../../src/Dango.Desktop/Accessories/Editing/Web
```

CI does not run the browser smoke or interactive fixtures. This check proves
bundle reproducibility and its static inventory, not editor interaction or
WPF/WebView2 acceptance. Preview ZIP packaging also requires the exact shipped
miniPaint/jQuery license texts and editor NOTICE, alongside Box2D/WebView2
legal materials, repository `LICENSE` and `THIRD-PARTY-NOTICES.md`, and the
runtime-pack licenses selected from the self-contained publish assets.

## Historical E0 API and privacy boundary

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

The upper preview tab now switches the central canvas into an editor-owned native
physical scene showing only the current accessory. A dedicated left tool rail provides
anchor/pan/zoom tools and a body refresh action; the upper inspector keeps wearing parameters. Image tools retract, and switching
back restores the image view. There is no bitmap frame stream or playback UI.
The body is draggable by default. The three left-side tools are anchor, pan and zoom;
clicking the active tool again returns to direct body dragging.
Refresh restores the temporary body pose and motion while keeping the camera and
accessory draft. While the independent editor is open, the control center's own
body owner stays paused and resumes only if its page remains eligible afterward.
The active editor restores focus to the physical world for the existing WASD/Space
controls, while fields and menus reserve their own input. Fine deformation settings
are always expanded in both modes, including the minimum supported window.
Wearing edits use the same history without exporting pixels; confirm returns the
complete draft and only outer Apply persists it. Hidden/failed scenes release
input and pause their sole native owner. Use
`npm run test:preview` for view transport/layout and `npm run test:multi-layer`
for shared layer transforms, merge, batch operations and retained rename paths.
Middle-joint deletion reconnects surviving neighbors and attached roots through
the same skeleton command/history path; `npm run test:skeleton-input` covers it.

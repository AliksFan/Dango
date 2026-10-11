# Private receipt-history candidate

This isolated native target revises only the private receipt import boundary. The original raw receipts remain in the diagnostic. Ordinary legal receipts use the unchanged contact solver/block. A single-point, post-restitution out-cone receipt can become retired history only when its live world/step/shape/body/point provenance and strictly detached geometry pass revalidation. It grants no current normal, friction, telemetry or rounding budget and no impulse is undone or replayed. Multi-point exceptional receipts remain rejected.

`capture.c` uses a candidate-only observer pointer on each generated Box2D world. It captures the last relax friction result, the actual restitution delta at the original computed-impulse location, and constraint/store/manifold identity. SIMD and overflow hooks share the same certificate rules. `retirement.h` freezes the original certificate and world/step/shape/body epoch in the view, and rebuilds classification and zero budgets on each gate. `adapter.inc` checks again before fresh double/float adjudication, the original speed limiter and writeback. All original solver/block arithmetic and thresholds remain unchanged.

`generate.py` requires an exact fingerprint of all 233 files in the pinned official Box2D tree. It creates a new source copy, rejects existing output directories, verifies the original contact solver/world code is exactly recovered when the insertion blocks are removed, and verifies reversing adapter dispatch additions recovers the original source. The manifest binds actual on-disk SHA256 values. The old source tree and build targets are never edited.

Build with the existing pinned source directory supplied externally; no machine path is stored in this recipe:

```powershell
python eng/native/box2d/receipt-history/generate.py --source <pinned-box2d-tree> --output <fresh-generated-directory>
cmake -S eng/native/box2d/receipt-history -B <fresh-build-directory> -G "Visual Studio 17 2022" -A x64 -DDANGO_RECEIPT_GENERATED=<absolute-generated-directory>
cmake --build <fresh-build-directory> --config Release
```

The candidate DLL is `dango_material_receipt_history_candidate.dll`, built with the unchanged final private physics definitions plus the observer definition, `/W4 /WX`, and `/Brepro`. It does not replace the production DLL. `dango_receipt_history_get_status` is a candidate-only observation export with history phase/status and cumulative retirement events.

History phases: 1 capture binding, 2 raw filter, 3 before solve, 4 before fresh double check, 5 before fresh float check, 6 before cap, 7 before writeback. Statuses: 0 unchecked, 1 valid, 2 invalid raw structure, 3 rejected source/classification, 4 failed revalidation. A pre-build history rejection reports block BUILD/shared RECEIPT with zero linear solves. A later rejection preserves the actual completed block result and separately reports the failed history phase; block OK is not a history acceptance flag. Capture binding refusal uses native phase 0 because no world integration ran.

Run the native test executable with five arguments: original 164-byte config, original 372-byte fixed-input state, old observed row binary, a fresh output row filename, and a fresh JSONL filename. It uses the actual live certificate at the former failure, checks the old rejection and filtered reference result, exercises provenance/current-budget/publication tampering, compares the complete rows, active IDs, modes, double/float outputs and cap report for 20 ordinary cases, compares the original pre-failure trajectory by float bits, and checks failure latch/last healthy snapshot/reset. Outputs use exclusive creation. `dll_probe.py` independently loads the built DLL and reruns the same 6000-frame trajectory against source-executable rows.

The reverse-order certificate evidence is a fixed original-source-kernel fixture with real node-A/ground-B identities and manifold. It uses the official capsule-circle primary contact order to obtain a capsule node A and circle ground B; the production node geometry is unchanged. The original overflow relax, restitution and store functions produce its certificate through the same source hooks; the fixture supplies fixed stage input velocities. It demonstrates nonzero stored tangent impulse agrees with the original collector, negative actual restitution delta and rejection of wrong tangent/normal signs. It is not evidence of an accepted reverse-world trajectory. The original collector canonicalizes its normal to +Y, so its tangent impulse equals the source stored tangent impulse for both shape orders; the certificate separately binds the source normal orientation.

These gates verify the bounded former failure and interface contract. They do not pass the complete S1/S2/S3 matrix, production integration or manual acceptance.

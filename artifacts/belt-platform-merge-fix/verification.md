# Saved platform continuous-certificate repair

2026-10-20. Base b2a024b; isolated branch codex/belt-platform-merge-fix. Native
platform geometry only. No gait, material, tolerance, depth, work budget,
projection cap, rollback gate or object geometry changed. No UI, global input,
merge, push, install or deployment. Frozen diagnosis evidence remains intact.

## Reproduced causes and repair

The frozen default 100/50/50 setup settles 180 ticks at x6m, inserts the saved
272x21px platform, walks right, and jumps at tick20. The original native code
fails at action tick63 (accepted world243, attempted244), -707/-605,
thetaDepth20/timeDepth16/work1272; see baseline-native.log.

1. Depth-first certification returned UNCERTAIN at the first undecided cell,
   hiding an actual penetration witness in pending cells. It now retains the
   unresolved outcome while searching under the same limits. A witness returns
   a correction candidate; any remaining unresolved cell prevents admission.
   Witness-free tangency still returns UNCERTAIN. Budget exhaustion still fails.
2. The accepted source can occupy the existing 0.5mm permitted shell. Raw AABB
   entry then lies before time0, so the old locator selected the endpoint exit
   face. Entry now uses the same forbidden interior as the certificate; the
   projection still targets the real surface plus the unchanged clearance.
   Certificate-only repair fails at the same tick with the wrong bottom normal;
   see fix-native-r2.log. Four directional unit cases exercise this separately.
3. Extending the other saved failures found a new endpoint tangency refusal at
   x5/jump120/action165; see saved-platform-600.log. The collection path computed
   256 candidate samples but ignored them and asked for clearance before
   correcting known penetration. It now shares the existing single-platform
   analytic face extrema, nonsmooth ties and corner locator through the small
   room_deepest_candidate helper. Candidates cannot pass admission: after all
   projections the full endpoint and original-old_q-to-corrected-q swept proofs,
   containment, material/path guards and all other surfaces remain mandatory.

## Validation

- saved-native-final.log: x6/jump20, x5/jump90, x5/jump120 each600 action ticks,
  each after180 settling ticks, plus an independent endpoint certificate and
  containment check after every successful action tick. All PASS.
- actual-saved-final.log: rebuilt actual DLL repeats all three cases, each600
  coherent successful collection frames. All PASS.
- room-contracts-final.log: frozen step63 from/to regression, four shell-entry
  directions, witness-free UNCERTAIN, interior/swept crossing/containment/budget
  refusal, atomicity, support coherence and complete rollback. PASS.
- body-equivalence-final.log: 9 configurations x480 no-platform ticks, all
  original successful read channels bitwise equal to current single-body DLL.
- collection-final.log: full existing collection API/geometry/contact/pair/wall/
  platform/support/removal/body-ball and whole-world rollback contracts. PASS.
- wall-final.log: both floor directions, aerial wall, platform-wall simultaneous
  contact,600 driven ticks and120 reverse-away ticks. PASS.
- static-platform-final.log: 9 configurations x480 standing ticks, real support
  and removal/fall. PASS.
- actual-disk-final.log: rebuilt actual DLL default disk3600 ticks, coherent
  frames and real body impulse. PASS.
- Negative controls intentionally FAIL: negative-early-uncertain-final.log
  restores only premature UNCERTAIN and fails the frozen from/to regression;
  negative-raw-entry.log restores only raw rectangle entry and fails the normal
  assertion; negative-contact-final.log disables actual contact and fails the
  independent endpoint certificate; negative-rollback-final.log corrupts object
  rollback and fails private-state identity. No passing gate was relaxed.
- C17 /W4 /WX build and git diff --check PASS. No new dependencies.

Reproduce after the existing native CMake configure (DANGO_BELT_ONLY=ON):

```powershell
cmake --build artifacts/belt-platform-merge-fix/build --config Release --target dango_belt_room dango_belt_room_tests_0 dango_belt_room_collection_tests_0
& artifacts/belt-platform-merge-fix/build/Release/dango_belt_room_tests_0.exe
& artifacts/belt-platform-merge-fix/build/Release/dango_belt_room_collection_tests_0.exe --saved-platform
& artifacts/belt-platform-merge-fix/build/Release/dango_belt_room_collection_tests_0.exe --saved-platform-dll artifacts/belt-platform-merge-fix/build/Release/dango_belt_room.dll
```

Built DLL: artifacts/belt-platform-merge-fix/build/Release/dango_belt_room.dll.
SHA256 1C50E89C9983949A089A5859A3E86D175EBF02A01CAA895657F7FC823C884A58.
Source and evidence binding: evidence-sha256.json. No product DLL was replaced.
No foreground presentation, real mouse/keyboard, product/manual or multiplayer
acceptance is established. No background test process remains at delivery.

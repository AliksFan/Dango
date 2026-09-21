"""Synthetic negative/positive evidence tests, never desktop acceptance."""

import copy
import unittest

import analyze_memory_diagnostic as analyzer


def fixture():
    rows = []
    resource = {"id": 1, "kind": "window", "type": "FictionalWindow", "roles": ["host.sceneWindow"],
                "currentlyReferenced": True, "weakAlive": True, "isDisposed": None,
                "disposedObserved": False, "detachedObserved": False}
    elapsed = 1.0
    cycle = 0
    phase = "warmup"

    def add(kind, age=0.0, advance=0.001, visible=True):
        nonlocal elapsed
        elapsed += advance
        allocated = round(elapsed * 1000)
        prior = rows[-1] if rows else None
        duration = elapsed - prior["elapsedSeconds"] if prior else None
        delta = allocated - prior["metrics"]["totalAllocatedBytes"] if prior else None
        metrics = dict.fromkeys(analyzer.RESOURCE_METRICS, 100)
        metrics["workingSetBytes"] = 200_000_000 + cycle * 1_000_000 + age * 100
        metrics["workingSetBytes"] = int(metrics["workingSetBytes"])
        metrics.update(totalAllocatedBytes=allocated, allocatedDeltaBytes=delta,
                       cpuSeconds=elapsed / 10, logicalProcessors=20,
                       cpuMachinePercent=((elapsed / 10 - prior["metrics"]["cpuSeconds"]) / duration / 20 * 100) if prior else None,
                       allocationIntervalSeconds=duration, allocationBytesPerSecond=delta / duration if prior else None,
                       collectionCounts=[2, 1, 0], collectionDeltas=[0, 0, 0] if prior else None,
                       resources=[copy.deepcopy(resource)], resourceCounts=analyzer.resource_counts([resource]), lastCompletedGc=None)
        rows.append({"diagnosticSchema": 1, "runKind": "memory-diagnostic", "recordKind": kind,
                     "elapsedSeconds": elapsed, "phaseAgeSeconds": age, "restores": cycle,
                     "phase": phase, "visible": visible, "runtimeRunning": visible,
                     "hostDisposed": phase == "disposed", "error": None, "metrics": metrics})

    def enter(name, visible=True):
        nonlocal phase
        phase = name
        add("phase-enter", visible=visible)

    def stable():
        enter("memory-stable")
        add("sample", 5, 5)
        add("sample", 10, 5)

    add("phase-enter")
    add("sample", 60, 60)
    stable()
    for next_cycle in range(1, 21):
        add("before-hide", 10)
        add("after-hide", 10, visible=False)
        enter("memory-cycle-hidden", visible=False)
        add("sample", 3, 3, visible=False)
        cycle = next_cycle
        add("before-restore", 3, visible=False)
        add("after-restore", 3)
        enter("memory-cycle-visible")
        add("sample", 3, 3)
        if cycle in analyzer.CHECKPOINTS:
            stable()
    add("before-dispose", 10)
    enter("disposed", visible=False)
    add("after-dispose", visible=False)
    add("sample", 30, 30, visible=False)
    evidence = {
        "memory-protocol": {"runKind": "memory-diagnostic", "stableSeconds": 10, "checkpointRestoreCounts": analyzer.CHECKPOINTS},
        "environment": {"mode": "Wallpaper", "processId": 123, "binaries": []},
        "complete": {"runKind": "memory-diagnostic", "restores": 20, "stableSeconds": 10},
        "disposal": {"schedulersStopped": True, "attachmentEntriesRemaining": 0, "attachmentRouterDisposed": True,
                     "runtimeRunning": False, "timersStopped": True, "runtimeDisposed": True,
                     "sceneReleased": True, "trayReleased": True, "windowsRemaining": 0},
        "process-exit": {"processId": 123, "exited": True, "exitCode": 0},
    }
    return rows, evidence


class MemoryDiagnosticTests(unittest.TestCase):
    def test_complete_describes_growth_without_acceptance_or_leak_verdict(self):
        result = analyzer.analyze(*fixture())
        self.assertEqual("complete", result["status"])
        self.assertFalse(result["acceptanceEvaluated"])
        self.assertEqual([], result["incompleteReasons"])
        self.assertAlmostEqual(20_000_000, result["checkpoints"]["20"]["tailMeanChangeFromBaseline"]["workingSetBytes"])
        self.assertAlmostEqual(1000, result["checkpoints"]["5"]["allocation"]["bytesPerSecond"])
        self.assertEqual([0, 0, 0], result["checkpoints"]["1"]["observedCollections"])
        self.assertNotIn("leak", result)

    def test_window_uses_same_protocol(self):
        rows, evidence = fixture()
        evidence["environment"]["mode"] = "Window"
        self.assertEqual("complete", analyzer.analyze(rows, evidence)["status"])

    def test_rate_uses_real_interval_not_assumed_one_second(self):
        rows, evidence = fixture()
        row = next(row for row in rows if row["recordKind"] == "sample")
        row["metrics"]["allocationBytesPerSecond"] = row["metrics"]["allocatedDeltaBytes"]
        result = analyzer.analyze(rows, evidence)
        self.assertEqual("invalid", result["status"])
        self.assertIn("allocation rate denominator", str(result["dataValidity"]["errors"]))

    def test_cpu_must_be_normalized_by_logical_processor_count(self):
        rows, evidence = fixture()
        rows[2]["metrics"]["cpuMachinePercent"] *= 20
        self.assertEqual("invalid", analyzer.analyze(rows, evidence)["status"])

    def test_regressing_allocation_or_gc_counts_are_invalid(self):
        for field, value in (("totalAllocatedBytes", 0), ("collectionCounts", [0, 0, 0])):
            with self.subTest(field=field):
                rows, evidence = fixture()
                rows[2]["metrics"][field] = value
                self.assertEqual("invalid", analyzer.analyze(rows, evidence)["status"])

    def test_resource_counts_cannot_claim_release_without_observation(self):
        rows, evidence = fixture()
        rows[-1]["metrics"]["resourceCounts"]["window"]["observedDisposed"] = 1
        self.assertEqual("invalid", analyzer.analyze(rows, evidence)["status"])

    def test_reused_identity_and_collected_identity_resurrection_are_invalid(self):
        for change in ("kind", "weakAlive"):
            rows, evidence = fixture()
            entry = rows[3]["metrics"]["resources"][0]
            if change == "kind":
                entry["kind"] = "hwnd-source"
            else:
                entry["weakAlive"] = False
                entry["currentlyReferenced"] = False
            rows[3]["metrics"]["resourceCounts"] = analyzer.resource_counts([entry])
            self.assertEqual("invalid", analyzer.analyze(rows, evidence)["status"])

    def test_missing_checkpoint_is_incomplete_not_shortened_to_pass(self):
        rows, evidence = fixture()
        for row in rows:
            if row["phase"] == "memory-stable" and row["restores"] == 10:
                row["phase"] = "unplanned"
        result = analyzer.analyze(rows, evidence)
        self.assertEqual("incomplete", result["status"])
        self.assertIn("checkpoint 10 incomplete", result["incompleteReasons"])

    def test_hidden_stable_sample_and_failed_restore_are_incomplete(self):
        for kind in ("sample", "after-restore"):
            rows, evidence = fixture()
            row = next(row for row in rows if row["recordKind"] == kind and row["restores"] == 1)
            row["visible"] = False
            self.assertEqual("incomplete", analyzer.analyze(rows, evidence)["status"])

    def test_external_exit_cannot_be_substituted_by_complete(self):
        for change in ("missing", "wrong-pid", "nonzero"):
            rows, evidence = fixture()
            if change == "missing":
                evidence.pop("process-exit")
            elif change == "wrong-pid":
                evidence["process-exit"]["processId"] = 456
            else:
                evidence["process-exit"]["exitCode"] = 11
            self.assertEqual("incomplete", analyzer.analyze(rows, evidence)["status"])

    def test_pending_weak_target_is_reported_without_proving_leak(self):
        rows, evidence = fixture()
        for row in rows:
            if row["phase"] == "disposed":
                resource = row["metrics"]["resources"][0]
                resource.update(currentlyReferenced=False, detachedObserved=True)
                row["metrics"]["resourceCounts"] = analyzer.resource_counts([resource])
        result = analyzer.analyze(rows, evidence)
        self.assertEqual("complete", result["status"])
        self.assertEqual(1, len(result["disposedObservation"]["detachedWeakAliveAtEnd"]))

    def test_error_and_failed_disposal_remain_incomplete(self):
        rows, evidence = fixture()
        evidence["disposal"]["windowsRemaining"] = 1
        rows[3]["error"] = "FictionalFailure"
        result = analyzer.analyze(rows, evidence)
        self.assertEqual("incomplete", result["status"])
        self.assertIn("missing or failed disposal evidence", result["incompleteReasons"])
        self.assertIn("runtime or watchdog failure", result["incompleteReasons"])

    def test_formal_schema_cannot_be_used_as_diagnostic_evidence(self):
        rows, evidence = fixture()
        rows[0] = {"schemaVersion": 2}
        self.assertEqual("invalid", analyzer.analyze(rows, evidence)["status"])


if __name__ == "__main__":
    unittest.main()

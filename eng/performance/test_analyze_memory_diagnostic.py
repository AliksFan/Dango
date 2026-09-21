"""Synthetic negative/positive evidence tests, never desktop acceptance."""

import copy
import contextlib
import io
import json
from pathlib import Path
import tempfile
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


def observed_counter_triplet():
    """Real allocation/GC/estimate values around the first observed anomaly; other fields synthetic."""
    seed = fixture()[0][0]
    observations = [
        # Leading baseline reconstructed from the preceding sample's delta/interval.
        (207.5803013, 1_989_917_040, [159, 23, 2], 2_090_032),
        (208.5762248, 1_999_596_816, [159, 23, 2], 2_090_032),
        (209.5765730, 2_009_634_608, [160, 23, 2], -348_224),
        (210.5763569, 2_019_903_376, [160, 23, 2], 13_949_992),
    ]
    rows = []
    for seconds, allocated, collections, managed in observations:
        row = copy.deepcopy(seed)
        row.update(elapsedSeconds=seconds, phase="memory-cycle-visible", restores=4, recordKind="sample")
        prior = rows[-1] if rows else None
        duration = seconds - prior["elapsedSeconds"] if prior else None
        delta = allocated - prior["metrics"]["totalAllocatedBytes"] if prior else None
        row["metrics"].update(managedBytesEstimate=managed, totalAllocatedBytes=allocated,
                              allocatedDeltaBytes=delta, allocationIntervalSeconds=duration,
                              allocationBytesPerSecond=delta / duration if prior else None,
                              collectionCounts=collections,
                              collectionDeltas=[value - old for value, old in zip(collections, prior["metrics"]["collectionCounts"])] if prior else None,
                              cpuSeconds=seconds / 10,
                              cpuMachinePercent=(seconds / 10 - prior["metrics"]["cpuSeconds"]) / duration / 20 * 100 if prior else None)
        rows.append(row)
    return rows


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

    def test_observed_negative_estimate_does_not_manufacture_allocation_errors(self):
        rows = observed_counter_triplet()
        original = copy.deepcopy(rows)
        errors, anomalies = analyzer.validate(rows)
        self.assertEqual([], errors)
        self.assertEqual(1, len(anomalies))
        self.assertEqual({"line": 3, "field": "managedBytesEstimate", "rawValue": -348_224,
                          "phase": "memory-cycle-visible", "restores": 4, "elapsedSeconds": 209.576573,
                          "reason": "nonnegative integer estimate unavailable; raw observation retained"}, anomalies[0])
        self.assertEqual(original, rows)

    def test_real_bad_delta_still_fails_without_contaminating_following_row(self):
        rows = observed_counter_triplet()
        rows[2]["metrics"]["allocatedDeltaBytes"] += 1
        errors, anomalies = analyzer.validate(rows)
        self.assertEqual([{"line": 3, "reason": "allocation delta"}], errors)
        self.assertEqual(-348_224, anomalies[0]["rawValue"])

    def test_all_three_observed_negative_values_remain_explicit_partial_evidence(self):
        rows, evidence = fixture()
        cycle_row = next(row for row in rows if row["phase"] == "memory-cycle-visible" and row["restores"] == 4 and row["recordKind"] == "sample")
        cycle_row["metrics"]["managedBytesEstimate"] = -348_224
        stable_rows = [row for row in rows if row["phase"] == "memory-stable" and row["restores"] == 5 and row["recordKind"] == "sample"]
        for row, value in zip(stable_rows, [-248_104, -695_120]):
            row["metrics"]["managedBytesEstimate"] = value
        original = copy.deepcopy(rows)
        result = analyzer.analyze(rows, evidence)
        self.assertEqual("partial", result["status"])
        self.assertEqual("partial", result["dataValidity"]["status"])
        self.assertEqual("complete", result["protocolStatus"])
        self.assertFalse(result["acceptanceEvaluated"])
        self.assertEqual([], result["dataValidity"]["errors"])
        self.assertEqual([-348_224, -248_104, -695_120], [item["rawValue"] for item in result["dataValidity"]["metricAnomalies"]])
        for segment in (result["lifecycleSegments"]["memory-cycle-visible/4"], result["checkpoints"]["5"]):
            estimate = segment["metrics"]["managedBytesEstimate"]
            self.assertEqual("metric-unavailable", estimate["status"])
            self.assertTrue(all(estimate[field] is None for field in ("first", "last", "peak", "lastMinusFirst", "tailWeightedMean")))
            self.assertEqual("available", segment["metrics"]["workingSetBytes"]["status"])
            self.assertGreater(segment["allocation"]["bytesPerSecond"], 0)
            self.assertGreater(segment["cpuMachinePercent"], 0)
        self.assertIsNone(result["checkpoints"]["5"]["tailMeanChangeFromBaseline"]["managedBytesEstimate"])
        self.assertEqual("available", result["checkpoints"]["10"]["metrics"]["managedBytesEstimate"]["status"])
        self.assertEqual(original, rows)

    def test_unavailable_baseline_only_suppresses_that_metric_comparison(self):
        rows, evidence = fixture()
        row = next(row for row in rows if row["phase"] == "memory-stable" and row["restores"] == 0)
        row["metrics"]["managedBytesEstimate"] = -348_224
        result = analyzer.analyze(rows, evidence)
        self.assertEqual("partial", result["status"])
        for checkpoint in result["checkpoints"].values():
            self.assertIsNone(checkpoint["tailMeanChangeFromBaseline"]["managedBytesEstimate"])
        self.assertAlmostEqual(20_000_000, result["checkpoints"]["20"]["tailMeanChangeFromBaseline"]["workingSetBytes"])

    def test_partial_metric_does_not_hide_invalid_counter_or_missing_exit(self):
        for defect in ("counter", "exit"):
            rows, evidence = fixture()
            rows[2]["metrics"]["managedBytesEstimate"] = -348_224
            if defect == "counter":
                rows[2]["metrics"]["allocatedDeltaBytes"] += 1
            else:
                evidence.pop("process-exit")
            result = analyzer.analyze(rows, evidence)
            self.assertEqual("invalid" if defect == "counter" else "incomplete", result["status"])
            self.assertFalse(result["acceptanceEvaluated"])
            self.assertEqual(1, len(result["dataValidity"]["metricAnomalies"]))

    def test_cli_preserves_original_invalid_report_and_raw_file(self):
        rows, evidence = fixture()
        rows[2]["metrics"]["managedBytesEstimate"] = -348_224
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            raw = "\n".join(json.dumps(row) for row in rows) + "\n"
            (root / "memory-samples.jsonl").write_text(raw, encoding="utf-8")
            for name, value in evidence.items():
                (root / f"{name}.json").write_text(json.dumps(value), encoding="utf-8")
            prior_report = '{"status":"invalid","historical":true}\n'
            (root / "analysis.json").write_text(prior_report, encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                code = analyzer.main([str(root), str(root / "analysis-v2.json")])
            self.assertEqual(1, code)
            self.assertEqual("partial", json.loads((root / "analysis-v2.json").read_text(encoding="utf-8"))["status"])
            self.assertEqual(raw, (root / "memory-samples.jsonl").read_text(encoding="utf-8"))
            self.assertEqual(prior_report, (root / "analysis.json").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()

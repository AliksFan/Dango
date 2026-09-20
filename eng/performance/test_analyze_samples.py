"""Contract and falsifiable gate tests; fixtures are explicitly synthetic."""

import contextlib
import copy
import datetime as dt
import io
import json
from pathlib import Path
import tempfile
import unittest

import analyze_samples as analyzer


ORIGIN = dt.datetime(2026, 1, 1, tzinfo=dt.timezone.utc)


def sample(second=1, state="idle", phase="observe", **overrides):
    row = {
        "elapsedSeconds": float(second), "intervalSeconds": 1.0, "phase": phase, "state": state,
        "cpuMachinePercent": 0.5, "workingSetBytes": 100_000_000, "privateBytes": 90_000_000,
        "handles": 100, "threads": 10, "gdiObjects": 12, "userObjects": 8,
        "visible": True, "runtimeRunning": True, "motionTimerEnabled": True, "motionTicks": 60,
        "appIntervalsMs": [16.0], "renderIntervalsMs": [], "error": None,
        "dozeSeconds": None, "cycle": 0, "utc": (ORIGIN + dt.timedelta(seconds=second)).isoformat(),
        "tickTotal": second * 60, "phaseAgeSeconds": float(second),
    }
    row.update(overrides)
    return row


def run_rows(seconds=3600):
    return [sample(index, "idle" if index <= seconds / 2 else "active") for index in range(1, seconds + 1)]


def complete_run():
    rows = []

    def append_phase(phase, seconds, state="idle", cycle=0):
        for age in range(1, seconds + 1):
            actual_state = state if state else "idle" if age <= seconds / 2 else "active"
            stopped = actual_state in ("hidden", "disposed")
            rows.append(sample(len(rows) + 1, actual_state, phase, cycle=cycle, phaseAgeSeconds=age,
                               visible=not stopped, runtimeRunning=not stopped, motionTimerEnabled=not stopped,
                               motionTicks=0 if stopped else 60, appIntervalsMs=[] if stopped else [16.0]))

    append_phase("warmup", 60)
    append_phase("observe", 3600, None)
    for cycle in range(1, 21):
        append_phase("cycle-hidden", 3, "hidden", cycle)
        append_phase("cycle-visible", 3, "idle", cycle)
    append_phase("hidden", 180, "hidden", 20)
    append_phase("restored", 180, "idle", 20)
    append_phase("disposed", 30, "disposed", 20)
    evidence = {
        "environment": {"startedUtc": ORIGIN.isoformat(), "mode": "Wallpaper", "observationSeconds": 3600,
                        "os": "Synthetic test OS", "architecture": "X64", "runtime": "Synthetic",
                        "logicalProcessors": 4, "screenCount": 1, "screens": [{"Width": 1920, "Height": 1080}],
                        "dpi": 96, "renderTier": 2,
                        "binaries": [{"name": name, "sha256": "a" * 64} for name in sorted(analyzer.MODULES)]},
        "complete": {"startedUtc": ORIGIN.isoformat(), "completedUtc": rows[-1]["utc"],
                     "elapsedSeconds": len(rows), "observationSeconds": 3600,
                     "tickTotal": rows[-1]["tickTotal"], "cycles": 20},
        "disposal": {"runtimeRunning": False, "timersStopped": True, "runtimeDisposed": True,
                     "sceneReleased": True, "trayReleased": True, "windowsRemaining": 0,
                     "attachmentsReleased": False, "attachmentEntriesRemaining": 0, "attachmentRouterDisposed": True},
        "hardware": {"Cpu": {"Name": "Synthetic test CPU"}},
        "process-exit": {"exited": True, "exitCode": 0},
    }
    return rows, evidence


class AnalysisTests(unittest.TestCase):
    def test_complete_synthetic_contract_satisfies_measured_gates(self):
        result = analyzer.analyze(run_rows())
        self.assertEqual(result["status"], "partial")
        self.assertEqual(result["observation"]["recordedSeconds"], 3600)
        self.assertEqual(result["observation"]["status"], "complete")
        self.assertEqual(result["states"]["idle"]["cpu"]["status"], "pass")
        self.assertEqual(result["states"]["active"]["cpu"]["status"], "pass")
        self.assertEqual(result["hiddenStopped"]["status"], "not-observed")
        self.assertIn("environment.json", result["fullRunEvidence"]["missing"])

    def test_short_state_window_cannot_pass_cpu(self):
        result = analyzer.analyze(run_rows(358), expected_observe_seconds=358)
        self.assertEqual(result["observation"]["status"], "complete")
        self.assertEqual(result["status"], "incomplete")
        self.assertEqual(result["states"]["idle"]["cpu"]["status"], "insufficient")
        self.assertEqual(result["states"]["active"]["cpu"]["status"], "insufficient")

    def test_thirty_minutes_cannot_count_as_sixty(self):
        result = analyzer.analyze(run_rows(1800))
        self.assertEqual(result["status"], "incomplete")
        self.assertEqual(result["observation"]["recordedSeconds"], 1800)
        self.assertIn("observation-too-short", result["observation"]["reasons"])

    def test_missing_field_marks_entire_evidence_invalid(self):
        rows = run_rows(360)
        del rows[10]["workingSetBytes"]
        result = analyzer.analyze(rows, 360)
        self.assertEqual(result["status"], "invalid")
        self.assertEqual(result["states"]["idle"]["cpu"]["status"], "invalid")
        self.assertEqual(result["states"]["active"]["memory"]["status"], "invalid")
        self.assertIn("missing workingSetBytes", result["dataValidity"]["errors"][0]["reasons"])

    def test_nonfinite_negative_and_wrong_types_are_invalid(self):
        invalid_values = [
            ("cpuMachinePercent", float("nan")), ("cpuMachinePercent", float("inf")),
            ("cpuMachinePercent", -0.1), ("workingSetBytes", -1), ("handles", True),
            ("intervalSeconds", 0), ("appIntervalsMs", [0]), ("appIntervalsMs", [float("inf")]),
            ("state", []), ("phase", {}), ("visible", 1), ("utc", "not-a-time"),
            ("utc", "2026-01-01T00:00:01"), ("dozeSeconds", -1),
        ]
        for field, value in invalid_values:
            with self.subTest(field=field, value=value):
                result = analyzer.analyze([sample(**{field: value})], 1)
                self.assertEqual(result["status"], "invalid")

    def test_cpu_and_decimal_memory_limits_fail(self):
        rows = run_rows(360)
        for row in rows:
            row["cpuMachinePercent"] = 1.01 if row["state"] == "idle" else 5.01
        rows[1]["workingSetBytes"] = 250_000_001
        result = analyzer.analyze(rows, 360)
        self.assertEqual(result["status"], "fail")
        self.assertEqual(set(result["failedGates"]), {"idle/cpu", "active/cpu", "idle/memory"})
        memory = result["states"]["idle"]["memory"]
        self.assertEqual(memory["limitBytes"], 250_000_000)
        self.assertAlmostEqual(memory["peakWorkingSetMiB"], 250_000_001 / 1048576)

    def test_exact_limits_pass_without_relaxation(self):
        rows = run_rows(360)
        for row in rows:
            row["cpuMachinePercent"] = 1 if row["state"] == "idle" else 5
            row["workingSetBytes"] = 250_000_000
        result = analyzer.analyze(rows, 360)
        self.assertEqual(result["status"], "partial")

    def test_cpu_uses_duration_weighting(self):
        rows = [sample(1, cpuMachinePercent=0), sample(4, intervalSeconds=3, cpuMachinePercent=4)]
        result = analyzer.analyze(rows, 4)
        self.assertEqual(result["states"]["idle"]["cpu"]["weightedMeanMachinePercent"], 3)

    def test_hidden_transition_is_excluded_but_settled_animation_fails(self):
        rows = [sample(1, "hidden", "hidden", phaseAgeSeconds=1),
                sample(2, "hidden", "hidden", phaseAgeSeconds=2, visible=False,
                       motionTimerEnabled=False, runtimeRunning=False, motionTicks=1)]
        result = analyzer.analyze(rows, 1)
        self.assertEqual(result["hiddenStopped"]["status"], "fail")
        self.assertEqual(result["hiddenStopped"]["transitionSamplesExcluded"], 1)
        self.assertEqual(result["hiddenStopped"]["settledSamples"], 1)
        self.assertIn("hidden/stopped", result["failedGates"])

    def test_each_hidden_signal_detects_a_failure(self):
        stopped = sample(2, "hidden", "cycle-hidden", intervalSeconds=2, visible=False,
                         runtimeRunning=False, motionTimerEnabled=False, motionTicks=0)
        self.assertEqual(analyzer.analyze([stopped], 1)["hiddenStopped"]["status"], "pass")
        for field in ("visible", "runtimeRunning", "motionTimerEnabled", "motionTicks"):
            row = copy.deepcopy(stopped)
            row[field] = 1 if field == "motionTicks" else True
            with self.subTest(field=field):
                self.assertEqual(analyzer.analyze([row], 1)["hiddenStopped"]["status"], "fail")

    def test_hidden_requires_phase_age(self):
        row = sample(1, "hidden", "hidden")
        del row["phaseAgeSeconds"]
        result = analyzer.analyze([row], 1)
        self.assertEqual(result["status"], "invalid")

    def test_gap_or_runtime_error_prevents_observation_completion(self):
        gap = [sample(1), sample(7, intervalSeconds=6)]
        result = analyzer.analyze(gap, 7)
        self.assertEqual(result["observation"]["status"], "incomplete")
        self.assertIn("sample-interval-over-5-seconds", result["observation"]["reasons"])
        self.assertEqual(result["states"]["idle"]["validGateSeconds"], 1)
        result = analyzer.analyze([sample(1, error="synthetic-error")], 1)
        self.assertIn("runtime-error", result["observation"]["reasons"])
        self.assertEqual(result["status"], "fail")

    def test_discontinuous_observe_blocks_do_not_form_continuous_run(self):
        rows = [sample(1), sample(2, phase="restored"), sample(3)]
        result = analyzer.analyze(rows, 2)
        self.assertIn("observation-is-not-contiguous", result["observation"]["reasons"])

    def test_duplicate_or_inconsistent_time_is_invalid(self):
        for rows in ([sample(1), sample(1)], [sample(1), sample(3)],
                     [sample(1), sample(2, utc=sample(1)["utc"])],
                     [sample(1, intervalSeconds=3)]):
            self.assertEqual(analyzer.analyze(rows, 1)["status"], "invalid")

    def test_percentile_and_fps_use_raw_interval_population(self):
        metrics = analyzer.interval_metrics(list(range(1, 21)), "application-update")
        self.assertEqual(metrics["meanMs"], 10.5)
        self.assertEqual(metrics["p95Ms"], 19)
        self.assertAlmostEqual(metrics["fps"], 20000 / 210)
        self.assertFalse(metrics["gpuPresentFps"])
        self.assertEqual(metrics["p95Method"], "nearest-rank")
        empty = analyzer.interval_metrics([], "application-update")
        self.assertIsNone(empty["fps"])

    def test_resource_growth_is_descriptive_and_phase_state_scoped(self):
        rows = [sample(index, handles=100 + index) for index in range(1, 181)]
        rows += [sample(index, "active", handles=10000) for index in range(181, 361)]
        result = analyzer.analyze(rows, 360)
        growth = result["phaseStates"]["observe/idle"]["resourceGrowth"]["handles"]
        self.assertEqual(growth["first60SecondsMean"], 130.5)
        self.assertEqual(growth["last60SecondsMean"], 250.5)
        self.assertEqual(growth["lastMinusFirst"], 120)
        self.assertEqual(growth["linearSlopePerHour"], 3600)
        self.assertEqual(growth["firstWindowCoveredSeconds"], 60)
        self.assertFalse(growth["windowsOverlap"])
        self.assertNotIn("status", growth)
        self.assertEqual(result["status"], "partial")

    def test_doze_has_no_invented_numeric_threshold(self):
        rows = [sample(index, "doze", cpuMachinePercent=3) for index in range(1, 181)]
        result = analyzer.analyze(rows, 180)
        self.assertEqual(result["states"]["doze"]["cpu"]["status"], "no-approved-threshold")
        self.assertIsNone(result["states"]["doze"]["cpu"]["limitMachinePercent"])

    def test_warmup_does_not_supply_state_gate_duration(self):
        rows = [sample(index, phase="warmup") for index in range(1, 181)]
        result = analyzer.analyze(rows, 180)
        self.assertEqual(result["states"]["idle"]["validGateSeconds"], 0)
        self.assertEqual(result["states"]["idle"]["cpu"]["status"], "insufficient")

    def test_cli_writes_safe_invalid_output_for_bad_json(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "samples.jsonl"
            output = Path(directory) / "summary.json"
            source.write_text("this is not json\n", encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                exit_code = analyzer.main([str(source), str(output)])
            result = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(exit_code, 2)
            self.assertEqual(result["status"], "invalid")
            self.assertNotIn(directory, output.read_text(encoding="utf-8"))

    def test_optional_render_array_and_bom_cli(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "samples.jsonl"
            output = Path(directory) / "summary.json"
            rows = run_rows(360)
            for row in rows:
                del row["renderIntervalsMs"]
            source.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8-sig")
            with contextlib.redirect_stdout(io.StringIO()):
                exit_code = analyzer.main([str(source), str(output), "--expected-observe-seconds", "360"])
            self.assertEqual(exit_code, 1)
            self.assertEqual(json.loads(output.read_text())["status"], "partial")

    def test_empty_input_is_invalid(self):
        self.assertEqual(analyzer.analyze([])["status"], "invalid")

    def test_complete_evidence_is_required_for_global_pass(self):
        rows, evidence = complete_run()
        result = analyzer.analyze(rows, evidence=evidence)
        self.assertEqual(result["status"], "pass", result["fullRunEvidence"])
        self.assertEqual(result["fullRunEvidence"]["status"], "complete")
        self.assertTrue(result["fullRunEvidence"]["attachmentReferenceRetained"])
        self.assertEqual(result["hiddenStopped"]["status"], "pass")
        for key in ("environment", "complete", "disposal", "hardware", "process-exit"):
            reduced = copy.deepcopy(evidence)
            del reduced[key]
            with self.subTest(missing=key):
                self.assertEqual(analyzer.analyze(rows, evidence=reduced)["status"], "partial")

    def test_reference_retention_does_not_falsely_fail_disposal(self):
        rows, evidence = complete_run()
        del evidence["disposal"]["attachmentEntriesRemaining"]
        del evidence["disposal"]["attachmentRouterDisposed"]
        result = analyzer.analyze(rows, evidence=evidence)
        self.assertEqual(result["status"], "partial")
        self.assertEqual(result["fullRunEvidence"]["disposal"]["attachments"], "not-observed")
        self.assertEqual(result["failedGates"], [])

    def test_disposal_failure_and_watchdog_marker_fail(self):
        rows, evidence = complete_run()
        evidence["disposal"]["windowsRemaining"] = 1
        evidence["watchdog-failure"] = {"kind": "synthetic"}
        result = analyzer.analyze(rows, evidence=evidence)
        self.assertEqual(result["status"], "fail")
        self.assertIn("disposal/windowsRemaining", result["failedGates"])
        self.assertIn("run/failure-marker", result["failedGates"])

    def test_cycle_and_scenario_duration_missing_cannot_pass(self):
        rows, evidence = complete_run()
        for row in rows:
            if row["phase"] == "cycle-visible" and row["cycle"] == 20:
                row["phaseAgeSeconds"] /= 2
            elif row["phase"] == "restored":
                row["phaseAgeSeconds"] /= 2
        result = analyzer.analyze(rows, evidence=evidence)
        self.assertEqual(result["status"], "partial")
        self.assertIn("cycle/20", result["fullRunEvidence"]["missing"])
        self.assertIn("scenario/restored", result["fullRunEvidence"]["missing"])

    def test_mixed_seconds_are_preserved_but_not_credited_to_single_state(self):
        rows = [sample(index, "idle" if index <= 90 else "mixed", cpuMachinePercent=10)
                for index in range(1, 361)]
        result = analyzer.analyze(rows, 360)
        self.assertEqual(result["dataValidity"]["status"], "valid")
        self.assertEqual(result["states"]["idle"]["validGateSeconds"], 90)
        self.assertEqual(result["states"]["idle"]["cpu"]["status"], "insufficient")
        self.assertEqual(result["states"]["mixed"]["cpu"]["status"], "mixed-not-a-single-state")
        self.assertEqual(result["phases"]["observe"]["validGateSeconds"], 360)

    def test_phase_age_prevents_borrowing_prior_phase_time(self):
        rows = [sample(index, phaseAgeSeconds=index - 0.2) for index in range(1, 361)]
        result = analyzer.analyze(rows, 360)
        self.assertAlmostEqual(result["observation"]["recordedSeconds"], 359.8)
        self.assertEqual(result["observation"]["status"], "incomplete")

    def test_bad_hash_is_invalid_metadata(self):
        rows, evidence = complete_run()
        evidence["environment"]["binaries"][0]["sha256"] = "not-a-hash"
        self.assertEqual(analyzer.analyze(rows, evidence=evidence)["status"], "invalid")

    def test_empty_artifact_object_cannot_pass(self):
        rows, evidence = complete_run()
        for key in ("environment", "complete", "disposal"):
            altered = copy.deepcopy(evidence)
            altered[key] = {}
            with self.subTest(key=key):
                self.assertEqual(analyzer.analyze(rows, evidence=altered)["status"], "partial")

    def test_below_target_application_fps_prevents_global_pass(self):
        rows, evidence = complete_run()
        for fps in (39.0, 59.999):
            for row in rows:
                if row["state"] == "active":
                    row["appIntervalsMs"] = [1000 / fps]
            with self.subTest(fps=fps):
                result = analyzer.analyze(rows, evidence=evidence)
                self.assertEqual(result["status"], "fail")
                self.assertIn("active/application-update-FPS-below-60", result["failedTargets"])
                self.assertEqual(result["states"]["active"]["applicationUpdateIntervals"]["targetStatus"], "below-target")
                self.assertFalse(result["states"]["active"]["applicationUpdateIntervals"]["gpuPresentFps"])

    def test_abnormal_process_exit_prevents_global_pass(self):
        rows, evidence = complete_run()
        for exit_data in ({"exited": False, "exitCode": 0}, {"exited": True, "exitCode": 10}):
            evidence["process-exit"] = exit_data
            with self.subTest(exit_data=exit_data):
                result = analyzer.analyze(rows, evidence=evidence)
                self.assertEqual(result["status"], "fail")
                self.assertEqual(result["fullRunEvidence"]["processExit"], "not-confirmed")


if __name__ == "__main__":
    unittest.main()

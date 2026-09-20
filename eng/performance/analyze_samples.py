#!/usr/bin/env python3
"""Summarize isolated desktop performance JSONL without third-party packages.

Usage: python analyze_samples.py samples.jsonl output.json
Exit codes: 0 = analysis complete with no failed/insufficient measured gate,
1 = failed gate or incomplete evidence, 2 = invalid input. Timing metrics are
application update/callback intervals, never GPU-present measurements.
"""

import argparse
import datetime as dt
import json
import math
from pathlib import Path
import re
import statistics
import sys


PHASES = {"warmup", "observe", "cycle-hidden", "cycle-visible", "hidden", "restored", "disposed"}
STATES = {"idle", "active", "doze", "hidden", "disposed", "mixed"}
RESOURCES = ("workingSetBytes", "privateBytes", "handles", "threads", "gdiObjects", "userObjects")
INTEGER_FIELDS = RESOURCES + ("motionTicks", "cycle", "tickTotal")
BOOLEAN_FIELDS = ("visible", "runtimeRunning", "motionTimerEnabled")
REQUIRED_FIELDS = {
    "elapsedSeconds", "intervalSeconds", "phase", "state", "cpuMachinePercent",
    "appIntervalsMs", "error", "dozeSeconds", "utc", *INTEGER_FIELDS, *BOOLEAN_FIELDS,
}
CPU_LIMITS = {"idle": 1.0, "active": 5.0}
MIN_STATE_SECONDS = 180.0
MAX_INTERVAL_SECONDS = 5.0
RESOURCE_WINDOW_SECONDS = 60.0
MODULES = {"Dango.Desktop.dll", "Dango.Core.dll", "Dango.Physics.dll",
           "dango_physics_module.dll", "dango_box2d.dll", "Dango.PerformanceProbe.dll"}


def finite_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def parse_utc(value):
    if not isinstance(value, str):
        raise ValueError("UTC must be a string")
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None or parsed.utcoffset() != dt.timedelta(0):
        raise ValueError("UTC must have zero offset")
    return parsed


def validate_sample(sample):
    """Return field-level reasons; never echo raw input or potentially sensitive errors."""
    if not isinstance(sample, dict):
        return ["sample must be an object"]
    errors = [f"missing {field}" for field in sorted(REQUIRED_FIELDS - sample.keys())]
    if errors:
        return errors
    for field in ("elapsedSeconds", "intervalSeconds", "cpuMachinePercent"):
        value = sample[field]
        if not finite_number(value) or value < 0 or (field == "intervalSeconds" and value == 0):
            errors.append(f"{field} must be finite and nonnegative (interval must be positive)")
    for field in INTEGER_FIELDS:
        value = sample[field]
        if not isinstance(value, int) or isinstance(value, bool) or value < 0:
            errors.append(f"{field} must be a nonnegative integer")
    for field in BOOLEAN_FIELDS:
        if not isinstance(sample[field], bool):
            errors.append(f"{field} must be boolean")
    if not isinstance(sample["phase"], str) or sample["phase"] not in PHASES:
        errors.append("unknown phase")
    if not isinstance(sample["state"], str) or sample["state"] not in STATES:
        errors.append("unknown state")
    if not isinstance(sample["error"], (str, type(None))):
        errors.append("error must be null or a string")
    if sample["dozeSeconds"] is not None and (
        not finite_number(sample["dozeSeconds"]) or sample["dozeSeconds"] < 0
    ):
        errors.append("dozeSeconds must be null or a finite nonnegative number")
    for field in ("appIntervalsMs", "renderIntervalsMs"):
        values = sample.get(field, [])
        if not isinstance(values, list) or any(not finite_number(v) or v <= 0 for v in values):
            errors.append(f"{field} must be an array of finite positive intervals")
    is_hidden = sample["state"] == "hidden" or sample["phase"] in ("hidden", "cycle-hidden")
    if is_hidden and "phaseAgeSeconds" not in sample:
        errors.append("missing phaseAgeSeconds for hidden sample")
    if "phaseAgeSeconds" in sample and (
        not finite_number(sample["phaseAgeSeconds"]) or sample["phaseAgeSeconds"] < 0
    ):
        errors.append("phaseAgeSeconds must be finite and nonnegative")
    try:
        parse_utc(sample["utc"])
    except (ValueError, TypeError, OverflowError):
        errors.append("utc must be an ISO 8601 UTC timestamp")
    if finite_number(sample["elapsedSeconds"]) and finite_number(sample["intervalSeconds"]):
        if sample["elapsedSeconds"] + 0.05 < sample["intervalSeconds"]:
            errors.append("interval starts before elapsed time origin")
    return errors


def interval_metrics(values, label):
    if not values:
        return {"kind": label, "count": 0, "meanMs": None, "p95Ms": None, "fps": None,
                "targetStatus": "not-observed"}
    ordered = sorted(values)
    fps = 1000.0 * len(values) / math.fsum(values)
    return {
        "kind": label,
        "count": len(values),
        "meanMs": statistics.fmean(values),
        "p95Ms": ordered[math.ceil(0.95 * len(ordered)) - 1],
        "p95Method": "nearest-rank",
        "fps": fps,
        "targetFps": 60.0,
        "fpsMinusTarget": fps - 60.0,
        "targetStatus": "below-target" if fps < 60.0 else "met-target",
        "gpuPresentFps": False,
    }


def weighted_mean(rows, field):
    duration = sum(row["intervalSeconds"] for row in rows)
    return sum(row[field] * row["intervalSeconds"] for row in rows) / duration if duration else None


def window_mean(rows, field, start, end):
    weights = [max(0.0, min(row["elapsedSeconds"], end) - max(
        row["elapsedSeconds"] - row["intervalSeconds"], start)) for row in rows]
    duration = sum(weights)
    return (sum(row[field] * weight for row, weight in zip(rows, weights)) / duration if duration else None,
            duration)


def resource_growth(rows):
    if not rows:
        return {}
    start = rows[0]["elapsedSeconds"] - rows[0]["intervalSeconds"]
    end = rows[-1]["elapsedSeconds"]
    mean_time = statistics.fmean(row["elapsedSeconds"] for row in rows)
    denominator = sum((row["elapsedSeconds"] - mean_time) ** 2 for row in rows)
    result = {}
    for field in RESOURCES:
        first, first_duration = window_mean(rows, field, start, min(start + RESOURCE_WINDOW_SECONDS, end))
        last, last_duration = window_mean(rows, field, max(start, end - RESOURCE_WINDOW_SECONDS), end)
        mean_value = statistics.fmean(row[field] for row in rows)
        slope = (sum((row["elapsedSeconds"] - mean_time) * (row[field] - mean_value)
                     for row in rows) / denominator * 3600.0) if denominator else None
        result[field] = {
            "first60SecondsMean": first,
            "last60SecondsMean": last,
            "lastMinusFirst": last - first if first is not None and last is not None else None,
            "linearSlopePerHour": slope,
            "firstWindowCoveredSeconds": first_duration,
            "lastWindowCoveredSeconds": last_duration,
            "windowsOverlap": end - start < 2 * RESOURCE_WINDOW_SECONDS,
        }
    return result


def summarize_group(rows, state, valid_input):
    # Warmup/disposed and interrupted/error intervals are not state gate evidence.
    usable = [row for row in rows if row["phase"] not in ("warmup", "disposed")
              and row["intervalSeconds"] <= MAX_INTERVAL_SECONDS and row["error"] is None]
    duration = sum(row["intervalSeconds"] for row in usable)
    cpu_mean = weighted_mean(usable, "cpuMachinePercent")
    cpu = {"weightedMeanMachinePercent": cpu_mean,
           "maximumSampleMachinePercent": max((row["cpuMachinePercent"] for row in usable), default=None),
           "limitMachinePercent": CPU_LIMITS.get(state), "minimumSeconds": MIN_STATE_SECONDS}
    if not valid_input:
        cpu["status"] = "invalid"
    elif duration < MIN_STATE_SECONDS:
        cpu["status"] = "insufficient"
    elif state == "mixed":
        cpu["status"] = "mixed-not-a-single-state"
    elif state not in CPU_LIMITS:
        cpu["status"] = "no-approved-threshold"
    else:
        cpu["status"] = "pass" if cpu_mean <= CPU_LIMITS[state] else "fail"
    peak = max((row["workingSetBytes"] for row in usable), default=None)
    memory = {"peakWorkingSetBytes": peak, "peakWorkingSetMB": peak / 1e6 if peak is not None else None,
              "peakWorkingSetMiB": peak / 1048576 if peak is not None else None,
              "limitBytes": 250_000_000 if state == "idle" else None}
    if not valid_input:
        memory["status"] = "invalid"
    elif not usable:
        memory["status"] = "insufficient"
    elif state != "idle":
        memory["status"] = "no-approved-threshold"
    else:
        memory["status"] = "pass" if peak <= 250_000_000 else "fail"
    return {
        "sampleCount": len(rows), "validGateSampleCount": len(usable), "validGateSeconds": duration,
        "cpu": cpu, "memory": memory,
        "applicationUpdateIntervals": interval_metrics(
            [value for row in usable for value in row["appIntervalsMs"]], "application-update"),
        "renderCallbackIntervals": interval_metrics(
            [value for row in usable for value in row.get("renderIntervalsMs", [])], "render-callback"),
        "resourceGrowth": resource_growth(usable),
    }


def scenario_seconds(rows):
    """Prefer phase age so the first interval cannot borrow time from a prior phase."""
    if not rows:
        return 0.0
    if all("phaseAgeSeconds" in row for row in rows):
        return min(sum(row["intervalSeconds"] for row in rows), max(row["phaseAgeSeconds"] for row in rows))
    return sum(row["intervalSeconds"] for row in rows)


def full_run_evidence(rows, evidence, expected_seconds):
    """Separate missing evidence from measured failures; retained references are not leaks."""
    missing, invalid, failed = [], [], []
    evidence = evidence or {}
    for name in ("environment", "complete", "disposal", "process-exit"):
        if name not in evidence:
            missing.append(f"{name}.json")
        elif not isinstance(evidence[name], dict):
            invalid.append(f"{name}.json must contain an object")
    env = evidence.get("environment") if isinstance(evidence.get("environment"), dict) else {}
    complete = evidence.get("complete") if isinstance(evidence.get("complete"), dict) else {}
    disposal = evidence.get("disposal") if isinstance(evidence.get("disposal"), dict) else {}
    process_exit = evidence.get("process-exit") if isinstance(evidence.get("process-exit"), dict) else {}
    for key, expected in {"exited": True, "exitCode": 0}.items():
        if key not in process_exit:
            missing.append(f"process-exit/{key}")
        elif type(process_exit[key]) is not type(expected):
            invalid.append(f"process-exit/{key} has wrong type")
        elif process_exit[key] != expected:
            failed.append(f"process-exit/{key}")
    if isinstance(evidence.get("environment"), dict):
        for key in ("startedUtc", "mode", "observationSeconds", "os", "architecture", "runtime",
                    "logicalProcessors", "screenCount", "screens", "dpi", "renderTier", "binaries"):
            if key not in env:
                missing.append(f"environment/{key}")
        for key in ("observationSeconds", "logicalProcessors", "screenCount", "dpi"):
            if key in env and (not finite_number(env[key]) or env[key] <= 0):
                invalid.append(f"environment/{key} must be positive and finite")
        if finite_number(env.get("observationSeconds")) and env["observationSeconds"] < expected_seconds:
            failed.append("environment/configured-observation-too-short")
        binaries = env.get("binaries")
        if isinstance(binaries, list):
            found = set()
            for binary in binaries:
                if not isinstance(binary, dict) or not isinstance(binary.get("name"), str) or not isinstance(binary.get("sha256"), str):
                    invalid.append("environment/binaries entry is malformed")
                    continue
                found.add(binary["name"])
                if not re.fullmatch(r"[0-9a-fA-F]{64}", binary["sha256"]):
                    invalid.append("environment/binaries SHA-256 is malformed")
            missing.extend(f"environment/module/{name}" for name in sorted(MODULES - found))
        elif "binaries" in env:
            invalid.append("environment/binaries must be an array")
    if isinstance(evidence.get("complete"), dict):
        for key in ("startedUtc", "completedUtc", "elapsedSeconds", "observationSeconds", "tickTotal", "cycles"):
            if key not in complete:
                missing.append(f"complete/{key}")
        for key in ("elapsedSeconds", "observationSeconds", "tickTotal", "cycles"):
            if key in complete and (not finite_number(complete[key]) or complete[key] < 0):
                invalid.append(f"complete/{key} must be nonnegative and finite")
        if finite_number(complete.get("observationSeconds")) and complete["observationSeconds"] < expected_seconds:
            failed.append("complete/configured-observation-too-short")
        if complete.get("cycles") != 20:
            missing.append("complete/20-cycles")
        if rows and finite_number(complete.get("elapsedSeconds")) and complete["elapsedSeconds"] < rows[-1]["elapsedSeconds"]:
            invalid.append("complete/elapsedSeconds precedes final sample")
        if rows and "tickTotal" in complete and complete["tickTotal"] != rows[-1]["tickTotal"]:
            invalid.append("complete/tickTotal differs from final sample")
        try:
            if "startedUtc" in complete and "completedUtc" in complete:
                if (parse_utc(complete["completedUtc"]) - parse_utc(complete["startedUtc"])).total_seconds() < expected_seconds:
                    failed.append("complete/wall-clock-too-short")
        except (ValueError, TypeError, OverflowError):
            invalid.append("complete/invalid-UTC-timestamps")
    disposal_checks = {}
    for key, expected in {"runtimeRunning": False, "timersStopped": True, "runtimeDisposed": True,
                          "sceneReleased": True, "trayReleased": True, "windowsRemaining": 0}.items():
        if key not in disposal:
            disposal_checks[key] = "not-observed"
            missing.append(f"disposal/{key}")
        elif type(disposal[key]) is not type(expected):
            disposal_checks[key] = "invalid"
            invalid.append(f"disposal/{key} has wrong type")
        else:
            disposal_checks[key] = "pass" if disposal[key] == expected else "fail"
            if disposal_checks[key] == "fail":
                failed.append(f"disposal/{key}")
    # Current product deliberately leaves its disposed attachment controller referenced.
    # A false attachmentsReleased flag alone says nothing about its internal entries/router.
    if disposal.get("attachmentsReleased") is True:
        disposal_checks["attachments"] = "pass"
    elif disposal.get("attachmentEntriesRemaining") == 0 and disposal.get("attachmentRouterDisposed") is True:
        disposal_checks["attachments"] = "pass"
    else:
        disposal_checks["attachments"] = "not-observed"
        missing.append("disposal/attachment-internal-release")
    phase_results = {}
    for phase, required in (("warmup", 60), ("hidden", 180), ("restored", 180), ("disposed", 30)):
        phase_rows = [row for row in rows if row["phase"] == phase]
        seconds = scenario_seconds(phase_rows)
        uninterrupted = all(row["intervalSeconds"] <= MAX_INTERVAL_SECONDS and row["error"] is None for row in phase_rows)
        status = "pass" if seconds >= required and uninterrupted else "insufficient"
        if status != "pass":
            missing.append(f"scenario/{phase}")
        phase_results[phase] = {"status": status, "seconds": seconds, "requiredSeconds": required}
    cycle_results = []
    for cycle in range(1, 21):
        counts = {}
        uninterrupted = True
        for phase in ("cycle-hidden", "cycle-visible"):
            phase_rows = [row for row in rows if row["cycle"] == cycle and row["phase"] == phase]
            counts[phase] = scenario_seconds(phase_rows)
            uninterrupted = uninterrupted and all(
                row["intervalSeconds"] <= MAX_INTERVAL_SECONDS and row["error"] is None for row in phase_rows)
        completed = all(value >= 3 for value in counts.values()) and uninterrupted
        if not completed:
            missing.append(f"cycle/{cycle}")
        cycle_results.append({"cycle": cycle, "status": "pass" if completed else "insufficient", **counts})
    visible_rows = [row for row in rows if row["phase"] in ("cycle-visible", "restored")
                    and row.get("phaseAgeSeconds", 0) >= 2]
    visible_failures = [row["elapsedSeconds"] for row in visible_rows if not row["visible"]
                        or not row["runtimeRunning"] or not row["motionTimerEnabled"]
                        or row["motionTicks"] == 0 or row["error"] is not None]
    if visible_failures:
        failed.append("restoration/not-running")
    if not visible_rows:
        missing.append("restoration/running-frames")
    if evidence.get("failure") is not None or evidence.get("watchdog-failure") is not None:
        failed.append("run/failure-marker")
    hardware = evidence.get("hardware")
    hardware_cpu = hardware.get("Cpu", {}) if isinstance(hardware, dict) else {}
    cpu_model_available = isinstance(hardware_cpu, dict) and isinstance(hardware_cpu.get("Name"), str) and bool(hardware_cpu["Name"].strip())
    if not cpu_model_available:
        missing.append("hardware/CPU-model")
    return {"status": "invalid" if invalid else "fail" if failed else "partial" if missing else "complete",
            "missing": missing, "invalid": invalid, "failed": failed, "phases": phase_results,
            "cycles": cycle_results, "disposal": disposal_checks,
            "restorationFailedSampleTimes": visible_failures, "cpuModelAvailable": cpu_model_available,
            "attachmentReferenceRetained": disposal.get("attachmentsReleased") is False,
            "processExit": "pass" if process_exit.get("exited") is True and type(process_exit.get("exitCode")) is int
            and process_exit["exitCode"] == 0 else "not-confirmed"}


def analyze(samples, expected_observe_seconds=3600.0, input_errors=None, evidence=None):
    if not finite_number(expected_observe_seconds) or expected_observe_seconds <= 0:
        raise ValueError("expected observe seconds must be finite and positive")
    errors = list(input_errors or [])
    valid = []
    previous = None
    for line_number, row in enumerate(samples, 1):
        problems = validate_sample(row)
        if problems:
            errors.append({"line": line_number, "reasons": problems})
            continue
        if previous is not None:
            elapsed_delta = row["elapsedSeconds"] - previous["elapsedSeconds"]
            if elapsed_delta <= 0:
                errors.append({"line": line_number, "reasons": ["elapsed time is not strictly increasing"]})
            elif abs(elapsed_delta - row["intervalSeconds"]) > 0.05:
                errors.append({"line": line_number, "reasons": ["interval does not match elapsed delta within 50 ms"]})
            if parse_utc(row["utc"]) <= parse_utc(previous["utc"]):
                errors.append({"line": line_number, "reasons": ["UTC time is not strictly increasing"]})
        valid.append(row)
        previous = row
    if not samples:
        errors.append({"line": None, "reasons": ["no samples"]})
    valid_input = not errors
    observed = [row for row in valid if row["phase"] == "observe"]
    seconds = scenario_seconds(observed)
    gaps = [row for row in observed if row["intervalSeconds"] > MAX_INTERVAL_SECONDS]
    runtime_errors = [row for row in observed if row["error"] is not None]
    reasons = []
    if not valid_input:
        reasons.append("invalid-input")
    if seconds < expected_observe_seconds:
        reasons.append("observation-too-short")
    if gaps:
        reasons.append("sample-interval-over-5-seconds")
    if runtime_errors:
        reasons.append("runtime-error")
    # Multiple observe blocks separated by another phase do not constitute one run.
    observe_indices = [index for index, row in enumerate(valid) if row["phase"] == "observe"]
    if observe_indices and len(observe_indices) != observe_indices[-1] - observe_indices[0] + 1:
        reasons.append("observation-is-not-contiguous")
    groups = {}
    for row in valid:
        groups.setdefault((row["phase"], row["state"]), []).append(row)
    phase_states = {f"{phase}/{state}": summarize_group(rows, state, valid_input)
                    for (phase, state), rows in sorted(groups.items())}
    states = {state: summarize_group([row for row in valid if row["state"] == state], state, valid_input)
              for state in sorted({row["state"] for row in valid})}
    phases = {phase: summarize_group([row for row in valid if row["phase"] == phase], "mixed", valid_input)
              for phase in sorted({row["phase"] for row in valid})}
    hidden_rows = [row for row in valid if row["state"] == "hidden"
                   or row["phase"] in ("hidden", "cycle-hidden")]
    settled_hidden = [row for row in hidden_rows if row["phaseAgeSeconds"] >= 2]
    hidden_failures = [{"elapsedSeconds": row["elapsedSeconds"], "cycle": row["cycle"],
                        "motionTicks": row["motionTicks"], "motionTimerEnabled": row["motionTimerEnabled"],
                        "runtimeRunning": row["runtimeRunning"], "visible": row["visible"]}
                       for row in settled_hidden if row["motionTicks"] != 0
                       or row["motionTimerEnabled"] or row["runtimeRunning"] or row["visible"]]
    hidden_status = ("invalid" if not valid_input else "not-observed" if not settled_hidden
                     else "fail" if hidden_failures else "pass")
    failed_gates = [f"{state}/{gate}" for state, result in states.items() for gate in ("cpu", "memory")
                    if result[gate]["status"] == "fail"]
    if hidden_status == "fail":
        failed_gates.append("hidden/stopped")
    if any(row["error"] is not None for row in valid):
        failed_gates.append("runtime/error")
    completeness = full_run_evidence(valid, evidence, expected_observe_seconds)
    failed_gates.extend(completeness["failed"])
    insufficient_gates = [f"{state}/cpu" for state in CPU_LIMITS
                          if state not in states or states[state]["cpu"]["status"] == "insufficient"]
    failed_targets = []
    active = states.get("active")
    if active and active["validGateSeconds"] >= MIN_STATE_SECONDS:
        active_fps = active["applicationUpdateIntervals"]["fps"]
        if active_fps is None:
            insufficient_gates.append("active/application-update-FPS")
        elif active_fps < 60:
            failed_targets.append("active/application-update-FPS-below-60")
    status = ("invalid" if not valid_input or completeness["invalid"] else "fail" if failed_gates or failed_targets else
              "incomplete" if reasons or insufficient_gates else "partial" if completeness["missing"] else "pass")
    return {
        "schemaVersion": 1, "status": status,
        "dataValidity": {"status": "valid" if valid_input else "invalid", "errors": errors,
                         "inputSampleCount": len(samples), "structurallyValidSampleCount": len(valid)},
        "observation": {"status": "complete" if not reasons else "incomplete", "reasons": reasons,
                        "expectedSeconds": expected_observe_seconds, "recordedSeconds": seconds,
                        "sampleCount": len(observed), "maximumIntervalSeconds": max(
                            (row["intervalSeconds"] for row in observed), default=None),
                        "intervalOver5SecondsCount": len(gaps), "runtimeErrorCount": len(runtime_errors)},
        "states": states, "phaseStates": phase_states, "phases": phases,
        "fullRunEvidence": completeness,
        "hiddenStopped": {"status": hidden_status, "settledSamples": len(settled_hidden),
                          "transitionSamplesExcluded": len(hidden_rows) - len(settled_hidden),
                          "failures": hidden_failures},
        "failedGates": failed_gates, "failedTargets": failed_targets, "insufficientGates": insufficient_gates,
        "limitations": [
            "Elapsed/UTC timestamps are recorded evidence; synthetic input cannot establish a real run.",
            "Application update and render callback intervals are not GPU present FPS.",
            "Resource first/last 60-second means and OLS slopes are descriptive, with no invented leak threshold.",
            "Passing observed gates does not establish manual acceptance, GPU, system sleep, or long-term stability.",
            "CPU is duration-weighted machine-normalized utilization; idle memory uses maximum observed working set.",
            "Mixed seconds are separate and never credited to a single-state CPU gate.",
            "A retained disposed attachment-controller reference does not establish a resource leak.",
        ],
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("samples", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--expected-observe-seconds", type=float, default=3600.0)
    args = parser.parse_args(argv)
    if args.samples.resolve() == args.output.resolve():
        parser.error("input and output must be different files")
    rows, errors = [], []
    try:
        with args.samples.open(encoding="utf-8-sig") as source:
            for line_number, line in enumerate(source, 1):
                try:
                    rows.append(json.loads(line))
                except (json.JSONDecodeError, ValueError):
                    rows.append(None)
                    errors.append({"line": line_number, "reasons": ["malformed JSON line"]})
        evidence = {}
        for name in ("environment", "complete", "disposal", "process-exit", "failure", "watchdog-failure"):
            path = args.samples.parent / f"{name}.json"
            if path.exists():
                with path.open(encoding="utf-8-sig") as source:
                    evidence[name] = json.load(source)
        hardware_path = args.samples.parent / "hardware.json"
        if not hardware_path.exists():
            hardware_path = args.samples.parent.parent / "hardware.json"
        if hardware_path.exists():
            with hardware_path.open(encoding="utf-8-sig") as source:
                evidence["hardware"] = json.load(source)
        result = analyze(rows, args.expected_observe_seconds, errors, evidence)
    except (OSError, UnicodeError, ValueError) as error:
        # Do not include error text: file exceptions can expose local usernames/paths.
        result = {"schemaVersion": 1, "status": "invalid",
                  "dataValidity": {"status": "invalid", "errors": [{"line": None, "reasons": [type(error).__name__]}]}}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps({"status": result["status"]}))
    return 2 if result["status"] == "invalid" else 0 if result["status"] == "pass" else 1


if __name__ == "__main__":
    sys.exit(main())

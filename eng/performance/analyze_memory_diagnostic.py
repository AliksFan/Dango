"""Describe isolated restoration-memory observations. Never evaluate performance acceptance."""

import argparse
from collections import Counter
import json
import math
from pathlib import Path
import sys


CHECKPOINTS = [0, 1, 5, 10, 20]
RESOURCE_METRICS = ("workingSetBytes", "privateBytes", "managedBytesEstimate", "handles",
                    "threads", "gdiObjects", "userObjects", "applicationWindowCount")
COUNT_FIELDS = ("observedIdentities", "currentlyReferenced", "weakAlive", "detachedWeakAlive",
                "collected", "observedDisposed", "observedDetached")


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def integer(value):
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


def resource_counts(resources):
    result = {}
    for entry in resources:
        counts = result.setdefault(entry["kind"], dict.fromkeys(COUNT_FIELDS, 0))
        counts["observedIdentities"] += 1
        counts["currentlyReferenced"] += entry["currentlyReferenced"]
        counts["weakAlive"] += entry["weakAlive"]
        counts["detachedWeakAlive"] += not entry["currentlyReferenced"] and entry["weakAlive"]
        counts["collected"] += not entry["weakAlive"]
        counts["observedDisposed"] += entry["disposedObserved"]
        counts["observedDetached"] += entry["detachedObserved"]
    return result


def validate(rows):
    errors, identities = [], {}
    previous = None
    for index, row in enumerate(rows, 1):
        try:
            assert row["diagnosticSchema"] == 1 and row["runKind"] == "memory-diagnostic", "diagnostic schema"
            assert number(row["elapsedSeconds"]) and row["elapsedSeconds"] >= 0, "elapsed seconds"
            assert number(row["phaseAgeSeconds"]) and row["phaseAgeSeconds"] >= 0, "phase age"
            assert integer(row["restores"]) and row["restores"] <= 20, "restore count"
            assert row["recordKind"] in {"sample", "phase-enter", "before-hide", "after-hide",
                                         "before-restore", "after-restore", "before-dispose", "after-dispose"}, "record kind"
            for field in ("visible", "runtimeRunning", "hostDisposed"):
                assert isinstance(row[field], bool), field
            metrics = row["metrics"]
            assert number(metrics["cpuSeconds"]) and metrics["cpuSeconds"] >= 0, "CPU seconds"
            assert integer(metrics["logicalProcessors"]) and metrics["logicalProcessors"] > 0, "logical processors"
            for field in (*RESOURCE_METRICS, "totalAllocatedBytes"):
                assert integer(metrics[field]), field
            collections = metrics["collectionCounts"]
            assert len(collections) == 3 and all(integer(value) for value in collections), "collection counts"
            if previous is None:
                assert all(metrics[field] is None for field in ("allocatedDeltaBytes", "allocationIntervalSeconds",
                                                               "allocationBytesPerSecond", "collectionDeltas")), "first counter delta"
                assert metrics["cpuMachinePercent"] is None, "first CPU delta"
            else:
                duration = row["elapsedSeconds"] - previous["elapsedSeconds"]
                assert duration > 0, "nonmonotonic time"
                prior = previous["metrics"]
                delta = metrics["totalAllocatedBytes"] - prior["totalAllocatedBytes"]
                assert delta >= 0 and metrics["allocatedDeltaBytes"] == delta, "allocation delta"
                assert number(metrics["allocationIntervalSeconds"]) and math.isclose(metrics["allocationIntervalSeconds"], duration, abs_tol=1e-7), "allocation interval"
                assert number(metrics["allocationBytesPerSecond"]) and math.isclose(metrics["allocationBytesPerSecond"], delta / duration, rel_tol=1e-7, abs_tol=1e-5), "allocation rate denominator"
                expected = [value - old for value, old in zip(collections, prior["collectionCounts"])]
                assert all(value >= 0 for value in expected) and metrics["collectionDeltas"] == expected, "collection delta"
                cpu_delta = metrics["cpuSeconds"] - prior["cpuSeconds"]
                assert cpu_delta >= 0 and metrics["logicalProcessors"] == prior["logicalProcessors"], "CPU counter regression"
                assert number(metrics["cpuMachinePercent"]) and math.isclose(metrics["cpuMachinePercent"], cpu_delta / duration / metrics["logicalProcessors"] * 100, rel_tol=1e-7, abs_tol=1e-5), "CPU normalization"
                if row.get("frameTotals") is not None and previous.get("frameTotals") is not None:
                    assert row["frameTotals"].keys() == previous["frameTotals"].keys(), "scheduler counter keys"
                    assert all(integer(value) and value >= previous["frameTotals"][key] for key, value in row["frameTotals"].items()), "scheduler counter regression"
            resources = metrics["resources"]
            assert len({entry["id"] for entry in resources}) == len(resources), "duplicate resource identity"
            for entry in resources:
                assert integer(entry["id"]) and entry["id"] > 0, "resource identity"
                for field in ("currentlyReferenced", "weakAlive", "disposedObserved", "detachedObserved"):
                    assert isinstance(entry[field], bool), "resource boolean"
                assert not entry["currentlyReferenced"] or entry["weakAlive"], "current resource collected"
                assert entry["isDisposed"] is not True or entry["disposedObserved"], "disposed observation lost"
                old = identities.get(entry["id"])
                if old:
                    assert entry["kind"] == old["kind"] and entry["type"] == old["type"], "identity reused"
                    assert old["weakAlive"] or not entry["weakAlive"], "collected identity resurrected"
                    assert not old["disposedObserved"] or entry["disposedObserved"], "disposed history regressed"
                    assert not old["detachedObserved"] or entry["detachedObserved"], "detachment history regressed"
                identities[entry["id"]] = entry
            assert set(identities) == {entry["id"] for entry in resources}, "identity history omitted"
            assert metrics["resourceCounts"] == resource_counts(resources), "resource count mismatch"
            previous = row
        except (AssertionError, KeyError, TypeError, ValueError) as error:
            errors.append({"line": index, "reason": str(error) if isinstance(error, AssertionError) else type(error).__name__})
    return errors


def summarize_segment(rows):
    first, last = rows[0], rows[-1]
    elapsed = last["elapsedSeconds"] - first["elapsedSeconds"]
    samples = [row for row in rows if row["recordKind"] == "sample"]
    tail_seconds = min(30.0, last["phaseAgeSeconds"] / 2)
    tail_start = last["phaseAgeSeconds"] - tail_seconds
    result = {"sampleCount": len(samples), "coveredSeconds": elapsed,
              "maximumSampleGapSeconds": max((right["elapsedSeconds"] - left["elapsedSeconds"]
                                                for left, right in zip(rows, rows[1:])), default=0),
              "metrics": {}}
    for field in RESOURCE_METRICS:
        tail = [(row["metrics"][field], max(0, row["phaseAgeSeconds"] - max(tail_start, prior["phaseAgeSeconds"])))
                for prior, row in zip(rows, rows[1:]) if row["recordKind"] == "sample"]
        covered = sum(weight for _, weight in tail)
        values = [row["metrics"][field] for row in rows]
        result["metrics"][field] = {"first": values[0], "last": values[-1], "peak": max(values),
                                      "lastMinusFirst": values[-1] - values[0],
                                      "tailWindowSeconds": covered,
                                      "tailWeightedMean": sum(value * weight for value, weight in tail) / covered if covered else None}
    result["allocation"] = {"deltaBytes": last["metrics"]["totalAllocatedBytes"] - first["metrics"]["totalAllocatedBytes"],
                            "intervalSeconds": elapsed}
    result["allocation"]["bytesPerSecond"] = result["allocation"]["deltaBytes"] / elapsed if elapsed else None
    result["observedCollections"] = [new - old for new, old in zip(last["metrics"]["collectionCounts"], first["metrics"]["collectionCounts"])]
    result["cpuMachinePercent"] = (last["metrics"]["cpuSeconds"] - first["metrics"]["cpuSeconds"]) / elapsed / last["metrics"]["logicalProcessors"] * 100 if elapsed else None
    result["schedulerTotalsAtStart"] = first.get("frameTotals")
    result["schedulerTotalsAtEnd"] = last.get("frameTotals")
    result["lastCompletedGcAtStart"] = first["metrics"].get("lastCompletedGc")
    result["lastCompletedGcAtEnd"] = last["metrics"].get("lastCompletedGc")
    result["resourceCountsAtStart"] = first["metrics"]["resourceCounts"]
    result["resourceCountsAtEnd"] = last["metrics"]["resourceCounts"]
    result["detachedWeakAliveAtEnd"] = [entry for entry in last["metrics"]["resources"] if entry["weakAlive"] and not entry["currentlyReferenced"]]
    return result


def analyze(rows, evidence):
    errors = validate(rows)
    result = {"diagnosticSchema": 1, "runKind": "memory-diagnostic", "status": "invalid" if errors else "incomplete",
              "dataValidity": {"status": "invalid" if errors else "valid", "errors": errors},
              "acceptanceEvaluated": False, "checkpoints": {}, "incompleteReasons": [],
              "limitations": [
                  "This is attribution evidence, never CPU, memory, FPS or long-duration acceptance.",
                  "Managed estimate is GC.GetTotalMemory(false); lastCompletedGc metadata is from the last completed GC.",
                  "Allocation is approximate cumulative process allocation including diagnostic overhead, not surviving memory.",
                  "Resource counts cover observed own-process identities, not all native/GPU/DWM objects or OS lifetime events.",
                  "Detached weak-alive or increasing private bytes alone do not prove a leak. No forced GC or trim is requested.",
                  "A baseline-relative increase or plateau is descriptive. Private bytes minus managed heap is not an exact native-leak size.",
                  "Product and existing observer references retain some host/window objects through disposal; this is not an unbiased whole-host collection test.",
                  "Resource kind is the first observed kind of each identity; roles overlap and must not be summed. Capturing and storing weak history adds allocations.",
                  "Recorded timestamps and synthetic analyzer tests cannot independently prove a real desktop run."]}
    if errors:
        return result
    reasons = result["incompleteReasons"]
    protocol = evidence.get("memory-protocol", {})
    seconds = protocol.get("stableSeconds")
    if not number(seconds) or seconds < 10 or protocol.get("checkpointRestoreCounts") != CHECKPOINTS or protocol.get("runKind") != "memory-diagnostic":
        reasons.append("missing or invalid memory protocol")
        return result
    result["stableSeconds"] = seconds
    result["mode"] = evidence.get("environment", {}).get("mode")
    if result["mode"] not in ("Window", "Wallpaper"):
        reasons.append("missing host mode")
    result["binaries"] = evidence.get("environment", {}).get("binaries", [])
    phase_rows = [row for row in rows if row["recordKind"] in ("phase-enter", "sample")]
    expected_phases = [("warmup", 0), ("memory-stable", 0)]
    for cycle in range(1, 21):
        expected_phases.extend([("memory-cycle-hidden", cycle - 1), ("memory-cycle-visible", cycle)])
        if cycle in CHECKPOINTS:
            expected_phases.append(("memory-stable", cycle))
    expected_phases.append(("disposed", 20))
    if [(row["phase"], row["restores"]) for row in rows if row["recordKind"] == "phase-enter"] != expected_phases:
        reasons.append("phase sequence differs from diagnostic protocol")
    for checkpoint in CHECKPOINTS:
        segment = [row for row in phase_rows if row["phase"] == "memory-stable" and row["restores"] == checkpoint]
        if not segment or segment[0]["recordKind"] != "phase-enter" or segment[-1]["phaseAgeSeconds"] < seconds:
            reasons.append(f"checkpoint {checkpoint} incomplete")
        if segment:
            if any(not row["visible"] or not row["runtimeRunning"] or row["hostDisposed"] for row in segment):
                reasons.append(f"checkpoint {checkpoint} not continuously visible/running")
            result["checkpoints"][str(checkpoint)] = summarize_segment(segment)
    baseline = result["checkpoints"].get("0")
    if baseline:
        for segment in result["checkpoints"].values():
            segment["tailMeanChangeFromBaseline"] = {field: segment["metrics"][field]["tailWeightedMean"] - baseline["metrics"][field]["tailWeightedMean"]
                                                     if segment["metrics"][field]["tailWeightedMean"] is not None and baseline["metrics"][field]["tailWeightedMean"] is not None else None
                                                     for field in RESOURCE_METRICS}
    transitions = Counter(row["recordKind"] for row in rows)
    result["transitionCounts"] = dict(transitions)
    for kind in ("before-hide", "after-hide", "before-restore", "after-restore"):
        if transitions[kind] != 20:
            reasons.append(f"{kind} count is not 20")
        expected_cycles = list(range(20)) if "hide" in kind else list(range(1, 21))
        if [row["restores"] for row in rows if row["recordKind"] == kind] != expected_cycles:
            reasons.append(f"{kind} restore-count order inconsistent")
    if any(row["visible"] or row["runtimeRunning"] for row in rows if row["recordKind"] == "after-hide"):
        reasons.append("hide requests were not all observed stopped")
    restores = [row for row in rows if row["recordKind"] == "after-restore"]
    if [row["restores"] for row in restores] != list(range(1, 21)) or any(not row["visible"] or not row["runtimeRunning"] for row in restores):
        reasons.append("restore requests were not all observed visible/running in order")
    for phase, cycles, minimum in (("warmup", [0], 60), ("memory-cycle-hidden", range(20), 3),
                                    ("memory-cycle-visible", range(1, 21), 3), ("disposed", [20], 30)):
        for cycle in cycles:
            segment = [row for row in phase_rows if row["phase"] == phase and row["restores"] == cycle]
            if not segment or segment[-1]["phaseAgeSeconds"] < minimum:
                reasons.append(f"{phase}/{cycle} incomplete")
            if phase in ("memory-cycle-hidden", "disposed") and any(row["visible"] or row["runtimeRunning"] for row in segment):
                reasons.append(f"{phase}/{cycle} did not stop")
            if phase in ("warmup", "memory-cycle-visible") and any(not row["visible"] or not row["runtimeRunning"] for row in segment):
                reasons.append(f"{phase}/{cycle} not visible/running")
    disposed_rows = [row for row in phase_rows if row["phase"] == "disposed"]
    if disposed_rows:
        result["disposedObservation"] = summarize_segment(disposed_rows)
    complete = evidence.get("complete", {})
    if complete.get("runKind") != "memory-diagnostic" or complete.get("restores") != 20 or complete.get("stableSeconds") != seconds:
        reasons.append("missing or inconsistent completion record")
    disposal = evidence.get("disposal", {})
    expected = {"schedulersStopped": True, "attachmentEntriesRemaining": 0, "attachmentRouterDisposed": True,
                "runtimeRunning": False, "timersStopped": True, "runtimeDisposed": True,
                "sceneReleased": True, "trayReleased": True, "windowsRemaining": 0}
    if any(disposal.get(field) != value for field, value in expected.items()):
        reasons.append("missing or failed disposal evidence")
    external = evidence.get("process-exit", {})
    if external.get("exited") is not True or external.get("exitCode") != 0 or not integer(external.get("processId")) or external.get("processId") != evidence.get("environment", {}).get("processId"):
        reasons.append("missing or failed matching external process exit")
    if any(row.get("error") for row in rows) or "failure" in evidence or "watchdog-failure" in evidence:
        reasons.append("runtime or watchdog failure")
    result["status"] = "incomplete" if reasons else "complete"
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args(argv)
    # Never overwrite raw evidence or another completed report.
    if args.output.exists():
        parser.error("output must be a new file")
    try:
        rows = [json.loads(line) for line in (args.directory / "memory-samples.jsonl").read_text(encoding="utf-8-sig").splitlines()]
        names = ("memory-protocol", "environment", "complete", "disposal", "process-exit", "failure", "watchdog-failure")
        evidence = {name: json.loads(path.read_text(encoding="utf-8-sig")) for name in names if (path := args.directory / f"{name}.json").exists()}
        result = analyze(rows, evidence)
    except (OSError, UnicodeError, ValueError) as error:
        result = {"diagnosticSchema": 1, "runKind": "memory-diagnostic", "status": "invalid", "acceptanceEvaluated": False,
                  "dataValidity": {"status": "invalid", "errors": [{"reason": type(error).__name__}]}}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        output.write(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n")
    print(json.dumps({"status": result["status"], "acceptanceEvaluated": False}))
    return 0 if result["status"] == "complete" else 2 if result["status"] == "invalid" else 1


if __name__ == "__main__":
    sys.exit(main())

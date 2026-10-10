"""Verify capture identity and reconstruct the copied solver rows independently.

This is a solver diagnostic, not a full-world replay or WPF acceptance gate.
"""
import argparse
import ctypes as ct
import json
import math
from pathlib import Path
import struct

import numpy as np


class Vec(ct.Structure):
    _fields_ = [("x", ct.c_float), ("y", ct.c_float)]


class Observation(ct.Structure):
    _fields_ = [(n, ct.c_int) for n in ("kind", "node", "bound", "event_kind", "mode", "count")]
    _fields_ += [("ids", ct.c_int * 4)]
    _fields_ += [(n, ct.c_bool) for n in (
        "correct", "value_available", "gradient_available", "solve_available", "multipliers_available",
        "touching", "violated", "clipped", "position_applied", "velocity_applied", "ground_direct_clamp")]
    _fields_ += [(n, ct.c_float) for n in (
        "measured", "bound_value", "signed_gap", "error", "touch_tolerance", "denominator", "rate",
        "raw_lambda", "lambda", "impulse", "maximum_move")]
    _fields_ += [("gradients", Vec * 4), ("inverse_mass", ct.c_float * 4)]
    _fields_ += [(n, Vec * 4) for n in (
        "positions_before", "positions_after", "velocities_before", "velocities_after")]


class Record(ct.Structure):
    _fields_ = [("frame", ct.c_int), ("small_step", ct.c_int), ("sweep", ct.c_uint64), ("row", Observation)]


def record_dtype():
    names, formats, offsets = [], [], []
    for name, typ in Record._fields_[:3]:
        names.append(name); formats.append("<u8" if typ is ct.c_uint64 else "<i4")
        offsets.append(getattr(Record, name).offset)
    for name, typ in Observation._fields_:
        names.append(name); offsets.append(Record.row.offset + getattr(Observation, name).offset)
        if typ is ct.c_int: formats.append("<i4")
        elif typ is ct.c_bool: formats.append("?")
        elif typ is ct.c_float: formats.append("<f4")
        elif name == "ids": formats.append(("<i4", (4,)))
        elif name == "inverse_mass": formats.append(("<f4", (4,)))
        else: formats.append(("<f4", (4, 2)))
    return np.dtype(dict(names=names, formats=formats, offsets=offsets, itemsize=ct.sizeof(Record)))


def read_lines(path):
    return [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines()]


def state_arrays(state, key):
    return np.asarray([b[key] for b in state["bodies"]], dtype=np.float32)


def equal_bits(a, b):
    return a.dtype == b.dtype and a.shape == b.shape and a.tobytes() == b.tobytes()


def velocity_residuals(state):
    """Separate length/second (normalized) and radians/second residuals."""
    p = state_arrays(state, "position").astype(np.float64)
    v = state_arrays(state, "velocity").astype(np.float64)
    radius = state["radialLength"]
    worst_length = worst_angle = worst_ground = 0.0
    active_rows = []
    for n in range(16):
        for kind, a, b, rest, lower, upper in (
            ("radial", 16, n, radius, state["radialMin"], state["radialMax"]),
            ("perimeter", n, (n+1) % 16, state["perimeterLength"], state["perimeterMin"], state["perimeterMax"]),
        ):
            d, dv = p[b]-p[a], v[b]-v[a]
            length = float(np.linalg.norm(d)); rate = float(np.dot(d, dv) / length)
            for bound, limit, closing in (("lower", rest*lower, -rate), ("upper", rest*upper, rate)):
                if abs(length-limit) <= 1e-5*radius:
                    worst_length = max(worst_length, max(0.0, closing) / radius)
                    active_rows.append(dict(kind=kind, node=n, bound=bound, gap=length-limit, closingRate=closing))
        gap = p[n,1] - state["ground"] - state["radius"]
        if gap <= 1e-5*radius:
            worst_ground = max(worst_ground, max(0.0, -v[n,1]) / radius)
        for display in (False, True):
            def angle(time):
                q = p + time*v
                if display:
                    d = q[:16] - q[16]
                    q[:16] += state["radius"] * d / np.linalg.norm(d, axis=1)[:,None]
                e, f = q[n]-q[(n+15)%16], q[(n+1)%16]-q[n]
                return math.atan2(e[0]*f[1]-e[1]*f[0], float(np.dot(e,f)))
            turn = angle(0)
            rate = (angle(1e-4)-angle(-1e-4)) / 2e-4
            if abs(turn-.002) <= 1e-5: worst_angle = max(worst_angle, max(0.0,-rate))
            if abs(turn-1.5687963268) <= 1e-5: worst_angle = max(worst_angle, max(0.0,rate))
    return dict(lengthNormalizedPerSecond=worst_length, angleRadiansPerSecond=worst_angle,
                groundNormalizedPerSecond=worst_ground, activeLengthRows=active_rows)


def verify_profile(plain, observed, actual):
    plain, observed = Path(plain), Path(observed)
    pm = json.loads(Path(str(plain)+".json").read_text())
    om = json.loads(Path(str(observed)+".json").read_text())
    for meta in (pm, om):
        assert meta["completedFrames"] == 42000 and not meta["error"] and not meta["latchedError"]
        assert not meta["captureFailed"]
    assert not pm["observerCompiled"] and om["observerCompiled"] and om["events"] > 0
    for suffix in (".frames.bin", ".cost.bin"):
        assert Path(str(plain)+suffix).read_bytes() == Path(str(observed)+suffix).read_bytes(), suffix+" differs"
    costs = np.fromfile(str(observed)+".cost.bin", dtype="<u8")
    assert costs.shape == (42000*2,) and (costs.reshape((-1,2)) > 0).all()
    frames = np.fromfile(str(observed)+".frames.bin", dtype="<f4").reshape((-1,42))
    assert frames.shape == (42000,42) and np.isfinite(frames).all()
    checkpoint_float_checks = 0
    # FullFrame ordering is intentionally explicit, not inferred from this capture.
    for checkpoint in actual["Checkpoints"]:
        s = checkpoint["FullFrame"]
        native = s["Contour"] + [s["CoreX"], s["CoreY"], s["SignedArea"], s["CentroidX"],
            s["CentroidY"], s["MaximumSpeed"], s["GroundContactCount"], int(s["IsGrounded"]), s["SelectedDragNode"], s["PointCount"]]
        assert equal_bits(frames[checkpoint["Metrics"]["WorldFrame"]-1], np.asarray(native,dtype=np.float32)), "actual DLL checkpoint differs"
        checkpoint_float_checks += 42
    names = ("CoreX", "CoreY", "Width", "Height")
    for i, name in enumerate(names):
        assert struct.pack("<d", om["spans"][i]) == struct.pack("<d", actual["Spans"][name])
    assert om["recordBytes"] == ct.sizeof(Record) and om["rowOffset"] == Record.row.offset
    assert om["observationBytes"] == ct.sizeof(Observation)
    events = np.memmap(str(observed)+".events.bin", mode="r", dtype=record_dtype())
    assert len(events) == om["events"]
    inputs = read_lines(str(observed)+".inputs.jsonl")
    stages = read_lines(str(observed)+".stages.jsonl")
    assert len(inputs) == len(stages) == 160
    selected = (1,600,601,721,781,1800,1801,3600,3601,36000)
    assert [(c["idleFrame"],c["smallStep"]) for c in inputs] == [(f,s) for f in selected for s in range(1,17)]
    assert all(c["idleFrame"] == c["worldFrame"]-6000 for c in inputs)
    ordered_end = 0; summaries = []
    for capture, stage in zip(inputs, stages):
        assert (capture["worldFrame"],capture["smallStep"]) == (stage["worldFrame"],stage["smallStep"])
        assert capture["outputEnergy"] == stage["afterConstraintsEnergy"]
        begin, count = capture["eventOffset"], capture["eventCount"]
        assert begin == ordered_end and count > 0; ordered_end = begin+count
        e = events[begin:ordered_end]
        assert (e["frame"] == capture["worldFrame"]).all() and (e["small_step"] == capture["smallStep"]).all()
        assert (e["count"] >= 1).all() and (e["count"] <= 4).all()
        assert (e["kind"] >= 1).all() and (e["kind"] <= 5).all()
        assert (e["node"] >= 0).all() and (e["node"] < 16).all()
        assert ((e["bound"] == 1) | (e["bound"] == 2)).all()
        assert ((e["event_kind"] == 0) | (e["event_kind"] == 1)).all()
        assert (e["mode"] == 0).all()
        participant = np.arange(4)[None,:] < e["count"][:,None]
        assert ((e["ids"][participant] >= 0) & (e["ids"][participant] < 17)).all()
        assert (e["sweep"] >= 1).all() and (e["sweep"] <= capture["sweeps"]).all()
        for quantity, key in (("positions","position"),("velocities","velocity")):
            initial, final = state_arrays(capture["input"],key), state_arrays(capture["output"],key)
            for n in range(17):
                row, col = np.where((e["ids"] == n) & (np.arange(4)[None,:] < e["count"][:,None]))
                assert len(row) > 0
                before, after = e[quantity+"_before"][row,col], e[quantity+"_after"][row,col]
                assert equal_bits(before[0],initial[n]), f"{quantity} initial chain node {n}"
                assert equal_bits(after[:-1],before[1:]), f"{quantity} discontinuous chain node {n}"
                assert equal_bits(after[-1],final[n]), f"{quantity} final chain node {n}"
        corrections = e[e["event_kind"] == 1]
        kind_counts = {str(k): int((corrections["kind"] == k).sum()) for k in range(1,6)}
        ground_contacts = [c for c in capture["contactsAfterWorldStep"] if c["groundIsA"] or c["groundIsB"]]
        for c in ground_contacts:
            norm = c["normalOnNode"]; tangent = c["rightTangentOnNode"]
            assert abs(norm[0]*tangent[0]+norm[1]*tangent[1]) < 1e-6
            for point in c["points"]: assert point["normalImpulse"] >= 0
        summaries.append(dict(idleFrame=capture["idleFrame"], smallStep=capture["smallStep"],
            sweeps=capture["sweeps"], correctionCounts=kind_counts, groundContacts=len(ground_contacts),
            before=velocity_residuals(capture["input"]), after=velocity_residuals(capture["output"]),
            afterDamping=velocity_residuals(stage["afterGroundDamping"]),
            kineticDeltaProjection=capture["outputEnergy"]["kinetic"]-capture["inputEnergy"]["kinetic"],
            gravityDeltaProjection=capture["outputEnergy"]["gravityPotential"]-capture["inputEnergy"]["gravityPotential"],
            kineticDeltaDamping=stage["afterGroundDampingEnergy"]["kinetic"]-stage["afterConstraintsEnergy"]["kinetic"]))
    assert ordered_end == len(events)
    assert om["reconstructionMaxPosition"] == om["reconstructionMaxVelocity"] == 0
    return dict(mirroredX=om["mirroredX"], completeFrameFloatChecks=42000*42,
                completeFrameCostChecks=42000*2, actualDllCheckpointFloatChecks=checkpoint_float_checks,
                actualDllSpanDoubleChecks=4, captures=len(inputs), rowEvents=len(events),
                spans=om["spans"], summaries=summaries)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prefix", required=True, help="Common prefix before plain/observed-forward/mirror")
    parser.add_argument("--actual", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    actual = json.loads(Path(args.actual).read_text())
    results = []
    for index, name in enumerate(("forward","mirror")):
        results.append(verify_profile(args.prefix+"plain-"+name, args.prefix+"observed-"+name, actual["trials"][index]))
    with Path(args.output).open("x",encoding="utf-8") as output:
        json.dump(dict(scope="Solver inputs and observed row chains; complete streams versus observer-off, selected checkpoints versus actual DLL.", profiles=results),output,indent=2)
    print("PASS: both complete trajectories and costs are bit exact; all observed row chains reconstruct final state.")


if __name__ == "__main__":
    main()

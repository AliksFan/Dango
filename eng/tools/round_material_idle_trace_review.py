#!/usr/bin/env python3
"""Independent review of the fixed private idle-displacement observation.

No world, DLL or solver is invoked here.  Values are read as their original IEEE
bits; position and velocity ledgers use exact integer binary grids.  The trace
reader is added once the writer's versioned schema has been frozen.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import re
import struct
import sys
from dataclasses import dataclass
from fractions import Fraction
from pathlib import Path

F32 = struct.Struct("<f")
U32 = struct.Struct("<I")
F64 = struct.Struct("<d")
U64 = struct.Struct("<Q")
FLOAT32_GRID_EXPONENT = 149
FLOAT64_GRID_EXPONENT = 1074
UNIT_ROUNDOFF = Fraction(1, 1 << 53)


class ReviewFailure(ValueError):
    """Malformed or nonconforming evidence, never a physics exception."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ReviewFailure(message)


def ieee_units(bits: int, width: int) -> int:
    """Return the exact finite IEEE value in its format's subnormal units."""
    require(width in (32, 64), "unsupported IEEE width")
    fraction_width, exponent_width = (23, 8) if width == 32 else (52, 11)
    require(0 <= bits < 1 << width, "invalid IEEE bit pattern width")
    fraction = bits & ((1 << fraction_width) - 1)
    exponent = (bits >> fraction_width) & ((1 << exponent_width) - 1)
    require(exponent != (1 << exponent_width) - 1, "nonfinite evidence number")
    magnitude = fraction if exponent == 0 else ((1 << fraction_width) | fraction) << (exponent - 1)
    return -magnitude if bits >> (width - 1) else magnitude


def value_bits(value: float, width: int) -> int:
    try:
        return (U32 if width == 32 else U64).unpack((F32 if width == 32 else F64).pack(value))[0]
    except (OverflowError, struct.error) as exc:
        raise ReviewFailure("value cannot be encoded in its declared format") from exc


def exact_value(bits: int, width: int) -> Fraction:
    return Fraction(ieee_units(bits, width), 1 << (149 if width == 32 else 1074))


def gamma(operation_count: int) -> Fraction:
    """Standard forward bound for n rounded binary64 operations, without a floor."""
    require(0 <= operation_count < 1 << 53, "invalid rounding-bound operation count")
    count_u = operation_count * UNIT_ROUNDOFF
    return count_u / (1 - count_u)


def sum_rounding_bound(values: list[Fraction], operation_count: int | None = None) -> Fraction:
    """Bound a specified binary64 sum against the independent exact ledger."""
    count = len(values) if operation_count is None else operation_count
    return gamma(count) * sum((abs(x) for x in values), Fraction())


def check_rounded_sum(recorded: float, exact_total: Fraction,
                      absolute_total: Fraction, operation_count: int, name: str) -> Fraction:
    require(math.isfinite(recorded), f"{name}: nonfinite reported total")
    error = abs(Fraction.from_float(recorded) - exact_total)
    allowance = gamma(operation_count) * absolute_total
    require(error <= allowance, f"{name}: reported total exceeds derived rounding bound")
    return allowance


def weighted_centroid(position_units: list[int], mass_units: list[int]) -> Fraction:
    require(len(position_units) == len(mass_units) == 17, "physical centroid requires all 17 bodies")
    require(all(m > 0 for m in mass_units), "nonpositive physical mass")
    return Fraction(sum(m * x for m, x in zip(mass_units, position_units)),
                    sum(mass_units) << FLOAT32_GRID_EXPONENT)


def _arithmetic_self_test() -> None:
    for width, values in ((32, (0.0, -0.0, 1.0, -0.72, 2.0 ** -149, 3.0e-4)),
                          (64, (0.0, -0.0, 1.0, -0.72, 2.0 ** -1074, 3.0e-4))):
        for value in values:
            bits = value_bits(value, width)
            rounded = (F32 if width == 32 else F64).unpack((F32 if width == 32 else F64).pack(value))[0]
            require(exact_value(bits, width) == Fraction.from_float(rounded), "IEEE exact-grid conversion")
    for width, bits in ((32, 0x7F800000), (32, 0x7FC00000),
                        (64, 0x7FF0000000000000), (64, 0x7FF8000000000000)):
        try:
            ieee_units(bits, width)
        except ReviewFailure:
            continue
        raise AssertionError("nonfinite negative was accepted")
    masses = [ieee_units(value_bits(0.015, 32), 32)] * 16 + [ieee_units(value_bits(0.12, 32), 32)]
    positions = [ieee_units(value_bits(i * 0.01, 32), 32) for i in range(17)]
    expected = sum((exact_value(value_bits(i * 0.01, 32), 32) *
                    exact_value(value_bits(0.015 if i < 16 else 0.12, 32), 32)
                    for i in range(17)), Fraction())
    expected /= sum((exact_value(value_bits(0.015 if i < 16 else 0.12, 32), 32)
                     for i in range(17)), Fraction())
    require(weighted_centroid(positions, masses) == expected, "exact mass-weighted centroid")
    require(gamma(0) == 0 and gamma(1) > UNIT_ROUNDOFF, "derived rounding bound")


HEADER_BYTES = 552
STEP_BYTES = 3300
MODE_BYTES = 32
COST_BYTES = 120
FRAME_BYTES = 184
END_BYTES = 4760
DIAGNOSTIC_BYTES = 87016
LIMIT_BYTES = 512 * 1024 * 1024
PREFIX = struct.Struct("<II")
COST = struct.Struct("<QIIii12Q")
MODE = struct.Struct("<IiI4BdQ")
RECEIPT = struct.Struct("<i4d")
STAGE_NAMES = ("continuity", "forces", "material_and_box2d", "position_correction",
               "damping", "contact_float", "cap", "writeback")
VELOCITY_NAMES = ("continuity", "forces", "material_and_box2d", "position_correction",
                  "damping", "contact_double", "double_to_float", "cap", "writeback")
COST_NAMES = ("position_iterations", "linear_solves", "double_full_checks", "float_checks",
              "mode_attempts", "pivot_searches", "pivot_eliminations", "certificate_calls",
              "coordinate_visits", "coefficient_visits", "residual_visits", "row_visits")
POSITION_BITS_OFFSET = 276
CONTACT_VELOCITY_OFFSET = 2452
RECEIPT_OFFSET = 2724
GRID32 = 1 << 149
GRID64 = 1 << 1074
PROMOTE_SHIFT = 1074 - 149


@dataclass(frozen=True)
class ExpectedRun:
    frames: int = 6301
    substeps: int = 16
    windows: bool = True
    first_gate: int | None = 6301

    @property
    def steps(self) -> int:
        return self.frames * self.substeps


def finite_units32(bits: int) -> int:
    exponent = (bits >> 23) & 255
    require(exponent != 255, "nonfinite float32 trace value")
    fraction = bits & 0x7FFFFF
    value = fraction if exponent == 0 else (0x800000 | fraction) << (exponent - 1)
    return -value if bits & 0x80000000 else value


def finite_units64(bits: int) -> int:
    exponent = (bits >> 52) & 2047
    require(exponent != 2047, "nonfinite float64 trace value")
    fraction = bits & 0xFFFFFFFFFFFFF
    value = fraction if exponent == 0 else (0x10000000000000 | fraction) << (exponent - 1)
    return -value if bits & 0x8000000000000000 else value


def ratio_json(value: Fraction) -> dict:
    # Decimal display is convenient; the numerator/denominator is authoritative.
    return {"value": float(value), "numerator": str(value.numerator), "denominator": str(value.denominator)}


def _fixed_frame(frame: int) -> bool:
    return frame <= 4 or 597 <= frame <= 604 or 6298 <= frame <= 6301


def _layout_document(document: dict) -> dict:
    supplied = document.get("layout", document)
    layout = {re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", key).lower(): value for key, value in supplied.items()}
    for name, size in (("header_bytes", HEADER_BYTES), ("step_bytes", STEP_BYTES),
                       ("mode_bytes", MODE_BYTES), ("frame_bytes", FRAME_BYTES),
                       ("diagnostic_bytes", DIAGNOSTIC_BYTES), ("config_bytes", 164),
                       ("cost_bytes", COST_BYTES)):
        require(layout.get(name) == size, f"layout {name}: incompatible packed schema")
    require(layout.get("schema") == 1, "unsupported layout schema")
    for name in ("input_offset", "positioned_offset", "damped_offset", "uncapped_offset",
                 "output_offset", "positions_offset", "velocities_offset", "inverse_mass_offset"):
        require(isinstance(layout.get(name), int) and layout[name] >= 0, f"missing layout {name}")
    worst = (2 * HEADER_BYTES + 100816 * (8 + STEP_BYTES + 32 * MODE_BYTES) +
             100816 * (8 + COST_BYTES) + 2 * 6301 * (8 + FRAME_BYTES) +
             2 * 258 * (8 + 24 + DIAGNOSTIC_BYTES) + 2 * (8 + END_BYTES) + 2097152)
    require(layout.get("worst_case_bytes") == worst and worst <= LIMIT_BYTES,
            "layout worst-case evidence capacity was not independently confirmed")
    return layout


class TraceReader:
    def __init__(self, file, role: int):
        self.file = file
        self.role = role
        self.bytes = 0
        self.hash = hashlib.sha256()
        self.record_count = 0
        raw = self.read_exact(HEADER_BYTES)
        head = struct.unpack_from("<8s8IQ", raw)
        require(head[0] == b"DGTRACE1" and head[1] == 1, "bad trace magic/schema")
        require(head[2] == role, "wrong ON/OFF role")
        require(head[3:9] == (HEADER_BYTES, STEP_BYTES, MODE_BYTES, FRAME_BYTES, DIAGNOSTIC_BYTES, 164),
                "trace header sizes differ from locked packed schema")
        require(head[9] == LIMIT_BYTES, "trace capacity differs from approved cap")
        self.header = raw
        self.mass_bits = struct.unpack_from("<17I", raw, 48)
        self.mass_values = struct.unpack_from("<17f", raw, 48)
        self.mass_units = [finite_units32(b) for b in self.mass_bits]
        require(all(x > 0 for x in self.mass_units), "header contains nonpositive actual mass")
        self.initial_bits = struct.unpack_from("<68I", raw, 116)
        self.initial_units = [finite_units32(b) for b in self.initial_bits]
        self.config = raw[388:552]

    def read_exact(self, count: int) -> bytes:
        require(0 <= count <= DIAGNOSTIC_BYTES + 24, "unbounded trace record read")
        data = self.file.read(count)
        require(len(data) == count, "truncated trace evidence")
        self.bytes += count
        require(self.bytes <= LIMIT_BYTES, "evidence file exceeds approved capacity")
        self.hash.update(data)
        return data

    def next(self) -> tuple[int, bytes]:
        kind, size = PREFIX.unpack(self.read_exact(8))
        valid = {1: (STEP_BYTES, STEP_BYTES + 32 * MODE_BYTES), 2: (FRAME_BYTES, FRAME_BYTES),
                 3: (24 + DIAGNOSTIC_BYTES, 24 + DIAGNOSTIC_BYTES),
                 4: (END_BYTES, END_BYTES), 5: (COST_BYTES, COST_BYTES)}
        require(kind in valid and valid[kind][0] <= size <= valid[kind][1], "invalid TLV kind/size")
        require((self.role == 1 and kind != 5) or (self.role == 0 and kind in (2, 4, 5)),
                "record violates ON/OFF schema")
        self.record_count += 1
        return kind, self.read_exact(size)

    def expect(self, kind: int) -> bytes:
        actual, payload = self.next()
        require(actual == kind, f"missing/duplicate/out-of-order record; expected type {kind}, got {actual}")
        return payload

    def finish(self) -> None:
        require(self.file.read(1) == b"", "trailing data after end record")


def _validate_meta(meta: tuple, sequence: int, expected: ExpectedRun) -> None:
    frame, sub = divmod(sequence - 1, expected.substeps)
    require(meta[:3] == (sequence, frame + 1, sub), "missing/duplicate/wrong-frame/wrong-substep record")
    require(meta[3:5] == (6, 0), "native step failed or did not reach actual writeback")
    costs = meta[5:]
    require(len(costs) == 12 and 1 <= costs[0] <= 384 and costs[1] <= 32 and costs[4] <= 32,
            "invalid original solver budget/cost metadata")


def _parse_step(payload: bytes, sequence: int, expected: ExpectedRun, mass_bits: tuple) -> dict:
    meta = COST.unpack_from(payload)
    _validate_meta(meta, sequence, expected)
    mask, receipts, events, active = struct.unpack_from("<4I", payload, 120)
    require(mask == 255, "missing or wrong-stage availability")
    require(receipts <= 16 and events <= 32 and active <= 16, "step exceeds fixed record capacity")
    require(len(payload) == STEP_BYTES + events * MODE_BYTES, "mode event count/payload mismatch")
    require(meta[9] == events, "mode history cost differs from observed ordered event count")
    dt, cap = struct.unpack_from("<2f", payload, 136)
    expected_dt = F32.unpack(F32.pack(F32.unpack(F32.pack(.0166666))[0] / expected.substeps))[0]
    require(value_bits(dt, 32) == value_bits(expected_dt, 32), "changed fixed small-step dt")
    require(math.isfinite(cap) and 0 < cap <= 1, "unavailable/invalid cap scale")
    ids = struct.unpack_from("<16i", payload, 144)[:active]
    require(tuple(sorted(set(ids))) == ids and all(0 <= i < 160 for i in ids), "invalid active physical row ids")
    require(struct.unpack_from("<17I", payload, 208) == mass_bits, "actual per-step mass differs from fixed mass source")
    bits = struct.unpack_from("<544I", payload, POSITION_BITS_OFFSET)
    values = struct.unpack_from("<544f", payload, POSITION_BITS_OFFSET)
    units = [finite_units32(b) for b in bits]
    double_bits = struct.unpack_from("<34Q", payload, CONTACT_VELOCITY_OFFSET)
    double_units = [finite_units64(b) for b in double_bits]
    double_values = struct.unpack_from("<34d", payload, CONTACT_VELOCITY_OFFSET)
    for body in range(17):
        for axis in range(2):
            require(bits[3 * 68 + body * 4 + 2 + axis] == bits[2 * 68 + body * 4 + 2 + axis],
                    "position-only correction changed copied velocity")
            require(value_bits(double_values[2 * body + axis], 32) == bits[5 * 68 + body * 4 + 2 + axis],
                    "contact double -> float conversion does not match uncapped actual float")
        for stage in (4, 5, 6, 7):
            require(bits[stage * 68 + body * 4:stage * 68 + body * 4 + 2] ==
                    bits[(stage - 1) * 68 + body * 4:(stage - 1) * 68 + body * 4 + 2],
                    "velocity-only stage moved position; possible duplicated correction or velocity/position swap")
        for component in range(4):
            require(bits[7 * 68 + body * 4 + component] == bits[6 * 68 + body * 4 + component],
                    "candidate output differs from actual body writeback")
        for axis in range(2):
            scaled = F32.unpack(F32.pack(cap * values[5 * 68 + body * 4 + 2 + axis]))[0]
            require(value_bits(scaled, 32) == bits[6 * 68 + body * 4 + 2 + axis], "cap actual float mismatch")
    receipt_data = []
    for index in range(receipts):
        row = RECEIPT.unpack_from(payload, RECEIPT_OFFSET + index * RECEIPT.size)
        require(0 <= row[0] < 16 and all(math.isfinite(v) for v in row[1:]), "invalid receipt data")
        require(row[1] >= 0 and row[2] >= 0 and row[4] >= 0, "negative friction/normal receipt telemetry")
        receipt_data.append(row)
    require(len({x[0] for x in receipt_data}) == receipts, "duplicate ground receipt node")
    event_data = []
    for index in range(events):
        event = MODE.unpack_from(payload, STEP_BYTES + index * MODE_BYTES)
        idx, status, flags, rank, nullity, dimension, count, alpha, modes = event
        require(idx == index and count == active and 0 <= dimension <= 16 and 0 <= rank <= dimension,
                "wrong-mode/dimension alpha event")
        require(flags & ~63 == 0 and flags & 1 and flags & 2, "unexecuted or failed mode solve in healthy trace")
        require(status == 0, "failed numerical certificate event")
        require(flags & 4 and dimension == active and active > 0 and rank + nullity == dimension,
                "healthy mode solve lacks complete matching factor metadata")
        require((nullity == 0 and flags == 7) or (nullity == 1 and flags == 63),
                "healthy rank-deficient solve omitted its actual alpha selection")
        require(all(((modes >> (4 * i)) & 15) <= 5 for i in range(active)), "invalid attempted mode bits")
        require(active == 16 or modes >> (4 * active) == 0, "mode bits beyond active columns")
        if flags & 4:
            require(rank + nullity == dimension, "inconsistent factor rank/nullity")
        if flags & 8:
            require(flags & 4 and flags & 16 and flags & 32 and nullity == 1 and dimension > 0 and
                    rank == dimension - 1 and math.isfinite(alpha), "unexecuted/invalid alpha counted as selected value")
        else:
            require(not flags & (16 | 32), "missing alpha presented as used/passed selection")
        event_data.append(event)
    return {"meta": meta, "mask": mask, "bits": bits, "values": values, "units": units,
            "double_units": double_units, "double_values": double_values, "events": event_data,
            "receipts": receipt_data, "cap": cap, "ids": ids}


class Ledger:
    def __init__(self, initial_units: list[int], masses: list[int]):
        self.initial = initial_units
        self.previous = initial_units
        self.mass = masses
        self.signed = [[0] * 34 for _ in range(8)]
        self.absolute = [[0] * 34 for _ in range(8)]
        self.sum_error64 = [[0] * 34 for _ in range(8)]
        self.rounded_absolute64 = [[0] * 34 for _ in range(8)]
        self.velocity = [[0] * 34 for _ in range(9)]
        self.velocity_absolute = [[0] * 34 for _ in range(9)]
        self.steps = 0
        self.nonexact_subtractions = 0

    def add(self, step: dict) -> None:
        units, values = step["units"], step["values"]
        for stage in range(8):
            before = units[(stage - 1) * 68:stage * 68] if stage else self.previous
            for body in range(17):
                for axis in range(2):
                    i = 2 * body + axis
                    diff = units[stage * 68 + body * 4 + axis] - before[body * 4 + axis]
                    self.signed[stage][i] += diff
                    self.absolute[stage][i] += abs(diff)
                    # The copied f32 endpoints can usually be subtracted exactly
                    # in binary64.  When they cannot, account for actual term rounding.
                    mag = abs(diff)
                    if not mag or mag.bit_length() - (mag & -mag).bit_length() + 1 <= 53:
                        rounded = diff << PROMOTE_SHIFT
                    else:
                        before_value = float(Fraction(before[body * 4 + axis], GRID32))
                        term = values[stage * 68 + body * 4 + axis] - before_value
                        rounded = finite_units64(value_bits(term, 64))
                        self.nonexact_subtractions += 1
                    self.sum_error64[stage][i] += abs(rounded - (diff << PROMOTE_SHIFT))
                    self.rounded_absolute64[stage][i] += abs(rounded)
        velocity_stages = [[self.previous[b * 4 + 2 + a] << PROMOTE_SHIFT for b in range(17) for a in range(2)]]
        for stage in range(8):
            if stage == 5:
                velocity_stages.append(step["double_units"])
            velocity_stages.append([units[stage * 68 + b * 4 + 2 + a] << PROMOTE_SHIFT
                                    for b in range(17) for a in range(2)])
        for stage in range(9):
            for i in range(34):
                diff = velocity_stages[stage + 1][i] - velocity_stages[stage][i]
                self.velocity[stage][i] += diff
                self.velocity_absolute[stage][i] += abs(diff)
        for body in range(17):
            for axis in range(2):
                require(sum(units[s * 68 + body * 4 + axis] -
                            (units[(s - 1) * 68 + body * 4 + axis] if s else self.previous[body * 4 + axis])
                            for s in range(8)) == units[7 * 68 + body * 4 + axis] - self.previous[body * 4 + axis],
                        "small-step displacement ledger does not telescope")
        self.previous = list(units[7 * 68:8 * 68])
        self.steps += 1

    def close(self, end_payload: bytes) -> dict:
        recorded = struct.unpack_from("<272d", end_payload, 136)
        absolute = struct.unpack_from("<272d", end_payload, 2312)
        maximum_error = Fraction()
        maximum_allowance = Fraction()
        for stage in range(8):
            for i in range(34):
                allowance = (gamma(self.steps) * Fraction(self.rounded_absolute64[stage][i], GRID64) +
                             Fraction(self.sum_error64[stage][i], GRID64))
                for reported, exact, label in ((recorded[stage * 34 + i], self.signed[stage][i], "signed"),
                                               (absolute[stage * 34 + i], self.absolute[stage][i], "absolute")):
                    require(math.isfinite(reported), "nonfinite C displacement summary")
                    error = abs(Fraction.from_float(reported) - Fraction(exact, GRID32))
                    require(error <= allowance, f"C {label} displacement differs from exact source ledger: stage{stage}/body{i//2}/axis{i%2}")
                    maximum_error = max(maximum_error, error)
                    maximum_allowance = max(maximum_allowance, allowance)
        for body in range(17):
            for axis in range(2):
                i = 2 * body + axis
                require(sum(self.signed[s][i] for s in range(8)) ==
                        self.previous[body * 4 + axis] - self.initial[body * 4 + axis], "whole-period body position ledger did not close")
                require(sum(self.velocity[s][i] for s in range(9)) ==
                        (self.previous[body * 4 + 2 + axis] - self.initial[body * 4 + 2 + axis]) << PROMOTE_SHIFT,
                        "whole-period body velocity ledger did not close")
        stages = []
        for s, name in enumerate(STAGE_NAMES):
            centroid = Fraction(sum(self.mass[b] * self.signed[s][2 * b] for b in range(17)),
                                sum(self.mass) * GRID32)
            stages.append({"name": name, "core_dx": ratio_json(Fraction(self.signed[s][32], GRID32)),
                           "physical_centroid_dx": ratio_json(centroid),
                           "body_signed_xy": [[float(Fraction(self.signed[s][2*b+a], GRID32)) for a in range(2)] for b in range(17)],
                           "body_absolute_xy": [[float(Fraction(self.absolute[s][2*b+a], GRID32)) for a in range(2)] for b in range(17)]})
        velocities = []
        for s, name in enumerate(VELOCITY_NAMES):
            momentum = Fraction(sum(self.mass[b] * self.velocity[s][2 * b] for b in range(17)), GRID32 * GRID64)
            velocities.append({"name": name, "core_dvx": ratio_json(Fraction(self.velocity[s][32], GRID64)),
                               "physical_horizontal_momentum_change": ratio_json(momentum),
                               "body_signed_velocity_xy": [[float(Fraction(self.velocity[s][2*b+a], GRID64)) for a in range(2)] for b in range(17)],
                               "body_absolute_velocity_xy": [[float(Fraction(self.velocity_absolute[s][2*b+a], GRID64)) for a in range(2)] for b in range(17)]})
        return {"position_stages": stages, "velocity_stages": velocities,
                "core_net_dx": ratio_json(Fraction(self.previous[64] - self.initial[64], GRID32)),
                "physical_centroid_net_dx": ratio_json(weighted_centroid(self.previous[0::4], self.mass) - weighted_centroid(self.initial[0::4], self.mass)),
                "c_summary_max_error": ratio_json(maximum_error), "c_summary_max_derived_bound": ratio_json(maximum_allowance),
                "nonexact_f32_endpoint_double_subtractions": self.nonexact_subtractions,
                "error_bound": "gamma_N*sum(abs(rounded endpoint differences))+sum(abs(actual subtraction rounding)); N=step count; u=2^-53; no floor"}


def _ledger_snapshot(ledger: Ledger) -> dict:
    return {"previous": ledger.previous[:], "steps": ledger.steps,
            **{name: [row[:] for row in getattr(ledger, name)]
               for name in ("signed", "absolute", "velocity", "velocity_absolute")}}


def _interval_ledger(ledger: Ledger, snapshot: dict) -> dict:
    differences = {name: [[value - snapshot[name][stage][i] for i, value in enumerate(row)]
                          for stage, row in enumerate(getattr(ledger, name))]
                   for name in ("signed", "absolute", "velocity", "velocity_absolute")}
    for name in ("absolute", "velocity_absolute"):
        require(all(v >= 0 for row in differences[name] for v in row), "interval absolute ledger was not cumulative")
    for body in range(17):
        for axis in range(2):
            i = body * 2 + axis
            require(sum(row[i] for row in differences["signed"]) ==
                    ledger.previous[body*4+axis] - snapshot["previous"][body*4+axis], "settled interval body displacement does not close")
            require(sum(row[i] for row in differences["velocity"]) ==
                    (ledger.previous[body*4+2+axis] - snapshot["previous"][body*4+2+axis]) << PROMOTE_SHIFT,
                    "settled interval body velocity does not close")
    result = {"from_frame_end": 601, "to_frame_end": 6301, "first_included_frame": 602,
              "included_small_steps": ledger.steps - snapshot["steps"],
              "core_start_x": float(Fraction(snapshot["previous"][64], GRID32)),
              "core_end_x": float(Fraction(ledger.previous[64], GRID32)),
              "core_net_dx": ratio_json(Fraction(ledger.previous[64] - snapshot["previous"][64], GRID32)),
              "physical_centroid_net_dx": ratio_json(weighted_centroid(ledger.previous[0::4], ledger.mass) -
                                                    weighted_centroid(snapshot["previous"][0::4], ledger.mass)),
              "position_stages": [], "velocity_stages": []}
    for stage, name in enumerate(STAGE_NAMES):
        signed = differences["signed"][stage]
        absolute = differences["absolute"][stage]
        result["position_stages"].append({"name": name, "core_dx": ratio_json(Fraction(signed[32], GRID32)),
            "core_absolute_dx": ratio_json(Fraction(absolute[32], GRID32)),
            "physical_centroid_dx": ratio_json(Fraction(sum(ledger.mass[b] * signed[2*b] for b in range(17)), sum(ledger.mass) * GRID32)),
            "body_signed_xy": [[float(Fraction(signed[2*b+a], GRID32)) for a in range(2)] for b in range(17)],
            "body_absolute_xy": [[float(Fraction(absolute[2*b+a], GRID32)) for a in range(2)] for b in range(17)]})
    for stage, name in enumerate(VELOCITY_NAMES):
        signed = differences["velocity"][stage]
        absolute = differences["velocity_absolute"][stage]
        result["velocity_stages"].append({"name": name, "core_dvx": ratio_json(Fraction(signed[32], GRID64)),
            "core_absolute_dvx": ratio_json(Fraction(absolute[32], GRID64)),
            "physical_horizontal_momentum_change": ratio_json(Fraction(sum(ledger.mass[b] * signed[2*b] for b in range(17)), GRID32 * GRID64)),
            "body_signed_velocity_xy": [[float(Fraction(signed[2*b+a], GRID64)) for a in range(2)] for b in range(17)],
            "body_absolute_velocity_xy": [[float(Fraction(absolute[2*b+a], GRID64)) for a in range(2)] for b in range(17)]})
    return result


def _window_review(payload: bytes, step: dict, layout: dict) -> tuple[int, int]:
    seq, frame, sub, reason, count = struct.unpack_from("<Q4I", payload)
    require((seq, frame, sub) == step["meta"][:3], "window assigned to wrong frame/substep")
    require(reason != 0 and reason & ~7 == 0 and count == DIAGNOSTIC_BYTES, "invalid window reason/size")
    diagnostic = payload[24:]
    phase, error, iterations, receipts = struct.unpack_from("<4i", diagnostic)
    require((phase, error, iterations, receipts) ==
            (step["meta"][3], step["meta"][4], step["meta"][5], len(step["receipts"])), "window phase/cost/receipt count mismatch")
    require(struct.unpack_from("<Q", diagnostic, 24)[0] == seq, "window diagnostic substep counter mismatch")
    for stage, name in ((2, "input"), (3, "positioned"), (4, "damped"), (5, "uncapped"), (6, "output")):
        base = layout[name + "_offset"]
        for field, component in (("positions", 0), ("velocities", 2)):
            offset = base + layout[field + "_offset"]
            require(offset + 136 <= len(diagnostic), "diagnostic offset exceeds original layout")
            raw = struct.unpack_from("<34I", diagnostic, offset)
            expected = tuple(step["bits"][stage * 68 + b * 4 + component + a] for b in range(17) for a in range(2))
            require(raw == expected, f"window {name}.{field} differs from compact copied boundary")
        offset = base + layout["inverse_mass_offset"]
        require(offset + 68 <= len(diagnostic), "inverse mass offset exceeds diagnostic layout")
        inverse_bits = struct.unpack_from("<17I", diagnostic, offset)
        for body in range(17):
            require(inverse_bits[body] == value_bits(1.0 / step["mass_values"][body], 32), "actual mass source differs from window solver float inverse_mass")
    return seq, reason


def _end_check(payload: bytes, reader: TraceReader, expected: ExpectedRun, cost_totals: list[int], windows: int) -> tuple:
    steps, frames, filebytes, n_windows, error, enabled = struct.unpack_from("<4QiI", payload)
    require((steps, frames, filebytes, n_windows, error, enabled) ==
            (expected.steps, expected.frames, reader.bytes, windows, 0, reader.role), "footer counts/bytes/native state invalid")
    require(list(struct.unpack_from("<12Q", payload, 40)) == cost_totals, "footer integer costs do not equal all observed small steps")
    final_bits = struct.unpack_from("<68I", payload, 4488)
    for bits in final_bits:
        finite_units32(bits)
    return final_bits


def review_streams(on_file, off_file, layout: dict, expected: ExpectedRun = ExpectedRun(),
                   patch=None, reference: dict | None = None) -> dict:
    on, off = TraceReader(on_file, 1), TraceReader(off_file, 0)
    require(on.header[48:] == off.header[48:], "ON/OFF actual initial state, masses or config differ")
    ledger = Ledger(on.initial_units, on.mass_units)
    totals = [0] * 12
    steps = frames = windows = events = zero_alpha = nonzero_alpha = missing_alpha = fullrank = 0
    first_nonzero = None
    alpha_min = alpha_max = None
    status_counts, contact_counts = {}, {}
    fixed_seen = set()
    exceptional_seen = set()
    last_step = None
    last_bits = on.initial_bits
    frame_start = list(on.initial_units)
    max_low = [None] * 4
    max_high = [None] * 4
    samples = []
    first_gate = None
    last_public_bits = None
    settled_snapshot = None
    settled_costs_start = None
    settled_alpha = {"events": 0, "selected_zero": 0, "selected_nonzero": 0, "unavailable": 0,
                     "factor_full_rank": 0, "first_nonzero": None, "minimum_selected": None,
                     "maximum_selected": None, "status_histogram": {}}
    settled_receipts = {}
    check_count = 0
    while True:
        kind, payload = on.next()
        if patch:
            replacement = patch(kind, payload, on.record_count)
            if replacement is None:
                continue
            kind, payload = replacement
        if kind == 1:
            require(steps < expected.steps and steps // expected.substeps == frames, "step/frame ordering differs from fixed execution")
            steps += 1
            step = _parse_step(payload, steps, expected, on.mass_bits)
            baseline = off.expect(5)
            require(payload[:120] == baseline, "ON/OFF native step metadata or integer solver costs differ")
            _validate_meta(COST.unpack(baseline), steps, expected)
            require(step["bits"][:68] == last_bits, "actual prior writeback does not connect to next begin")
            step["mass_values"] = on.mass_values
            ledger.add(step)
            last_bits = step["bits"][7 * 68:8 * 68]
            last_step = step
            for i, value in enumerate(step["meta"][5:]):
                totals[i] += value
            for event in step["events"]:
                events += 1
                idx, status, flags, rank, nullity, dim, active, alpha, modes = event
                key = f"status={status},flags={flags},rank={rank},nullity={nullity},dimension={dim}"
                status_counts[key] = status_counts.get(key, 0) + 1
                if flags & 8:
                    zero_alpha += alpha == 0
                    nonzero_alpha += alpha != 0
                    alpha_min = alpha if alpha_min is None else min(alpha_min, alpha)
                    alpha_max = alpha if alpha_max is None else max(alpha_max, alpha)
                    if alpha != 0 and first_nonzero is None:
                        first_nonzero = {"sequence": steps, "frame": step["meta"][1], "substep": step["meta"][2], "mode_index": idx, "alpha": alpha}
                else:
                    missing_alpha += 1
                    fullrank += bool(flags & 4 and rank == dim and nullity == 0)
                if step["meta"][1] > 601:
                    settled_alpha["events"] += 1
                    histogram = settled_alpha["status_histogram"]
                    histogram[key] = histogram.get(key, 0) + 1
                    if flags & 8:
                        settled_alpha["selected_zero" if alpha == 0 else "selected_nonzero"] += 1
                        old_min, old_max = settled_alpha["minimum_selected"], settled_alpha["maximum_selected"]
                        settled_alpha["minimum_selected"] = alpha if old_min is None else min(alpha, old_min)
                        settled_alpha["maximum_selected"] = alpha if old_max is None else max(alpha, old_max)
                        if alpha != 0 and settled_alpha["first_nonzero"] is None:
                            settled_alpha["first_nonzero"] = {"sequence": steps, "frame": step["meta"][1],
                                                             "substep": step["meta"][2], "mode_index": idx, "alpha": alpha}
                    else:
                        settled_alpha["unavailable"] += 1
                        settled_alpha["factor_full_rank"] += bool(flags & 4 and rank == dim and nullity == 0)
            contact_counts[str(len(step["receipts"]))] = contact_counts.get(str(len(step["receipts"])), 0) + 1
            if step["meta"][1] > 601:
                key = str(len(step["receipts"]))
                settled_receipts[key] = settled_receipts.get(key, 0) + 1
            check_count += 1
        elif kind == 3:
            require(last_step is not None, "window before any native step")
            seq, reason = _window_review(payload, last_step, layout)
            require(seq not in fixed_seen and seq not in exceptional_seen, "duplicate full window diagnostic")
            fixed = _fixed_frame(last_step["meta"][1])
            require(bool(reason & 1) == fixed, "fixed diagnostic window marked incorrectly")
            if reason & 1:
                fixed_seen.add(seq)
            if reason & 2:
                require(first_nonzero is not None and first_nonzero["sequence"] == seq,
                        "first-nonzero alpha diagnostic is not chronological first")
            require(not reason & 4, "native error diagnostic in healthy fixed default trajectory")
            if reason & ~1:
                exceptional_seen.add(seq)
            windows += 1
        elif kind == 2:
            require(steps == (frames + 1) * expected.substeps, "missing native substep before public frame")
            frames += 1
            baseline = off.expect(2)
            require(payload == baseline, "ON/OFF public frame metadata or raw float bits differ")
            frame, published, error, contacts = struct.unpack_from("<IIii", payload)
            require((frame, published, error) == (frames, 1, 0), "public frame missing/unpublished/native failure")
            row_bits = struct.unpack_from("<42I", payload, 16)
            row_values = struct.unpack_from("<42f", payload, 16)
            for bits in row_bits:
                finite_units32(bits)
            require(row_bits[32:34] == (last_bits[64], last_bits[65]), "public core differs from actual body writeback")
            for body in range(17):
                for axis in range(2):
                    i = 2 * body + axis
                    current = ledger.previous[body * 4 + axis]
                    require(current - frame_start[body * 4 + axis] ==
                            sum(ledger.signed[s][i] for s in range(8)) -
                            (frame_start[body * 4 + axis] - ledger.initial[body * 4 + axis]), "outer-frame exact displacement closure failed")
            frame_start = list(ledger.previous)
            if frames == 601:
                settled_snapshot = _ledger_snapshot(ledger)
                settled_costs_start = totals[:]
            index = frames - 1
            if index >= 600 and (index % 60 == 0 or index >= 35400):
                values = [row_values[32], row_values[33], max(row_values[0:32:2]) - min(row_values[0:32:2]),
                          max(row_values[1:32:2]) - min(row_values[1:32:2])]
                for i in range(4):
                    max_low[i] = values[i] if max_low[i] is None else min(max_low[i], values[i])
                    max_high[i] = values[i] if max_high[i] is None else max(max_high[i], values[i])
                spans = [max_high[i] - max_low[i] for i in range(4)]
                samples.append({"frame": frames, "core_x": values[0], "core_y": values[1], "width": values[2], "height": values[3], "spans": spans})
                if first_gate is None and any(v >= .003 for v in spans):
                    first_gate = {"frame": frames, "spans": spans}
            last_public_bits = row_bits
        elif kind == 4:
            require(steps == expected.steps and frames == expected.frames, "trace ended before complete fixed run")
            baseline = off.expect(4)
            final_on = _end_check(payload, on, expected, totals, windows)
            final_off = _end_check(baseline, off, expected, totals, 0)
            require(final_on == final_off == last_bits, "ON/OFF footer actual state differs from last written state")
            require(baseline[136:4488] == bytes(4352), "OFF footer contains unexecuted observation ledger")
            on.finish()
            off.finish()
            summary = ledger.close(payload)
            break
        else:
            raise ReviewFailure("unexpected ON record kind")
    if expected.windows:
        required_windows = {(frame - 1) * expected.substeps + sub + 1
                            for frame in range(1, expected.frames + 1) if _fixed_frame(frame)
                            for sub in range(expected.substeps)}
        require(fixed_seen == required_windows, "fixed windows omit or add a native substep")
        require(windows <= len(required_windows) + 2, "full diagnostics exceed approved fixed/exceptional windows")
        if first_nonzero:
            require(first_nonzero["sequence"] in exceptional_seen, "first nonzero alpha has no complete fixed diagnostic")
    require((first_gate or {}).get("frame") == expected.first_gate, "original .003 first-exceedance was not reproduced")
    if expected == ExpectedRun():
        require(len(samples) == 96, "original sampling schedule does not contain 96 fixed-prefix points")
        require(first_gate["spans"][0] == 0.0030154224950820208, "fixed-prefix core x first-exceedance changed")
    if reference is not None:
        require(reference.get("actualDll") and reference.get("completedFrames") == expected.frames,
                "reference is not the previously recorded actual-DLL frame6301 capture")
        reference_bits = tuple(value_bits(v, 32) for v in reference["nativeRow"])
        require(last_public_bits == reference_bits, "final 42 public bits differ from retained pre-observation actual DLL capture")
        ref = reference["lastSubstep"]
        for stage, name in ((2, "input"), (3, "positioned"), (4, "damped"), (5, "uncapped"), (6, "output")):
            stored = ref[name]["bodies"]
            require(tuple(value_bits(v, 32) for body in stored for v in body["position"] + body["velocity"]) ==
                    last_step["bits"][stage * 68:(stage + 1) * 68], "final boundary differs from retained actual DLL substep")
        require(tuple(value_bits(v, 64) for body in ref["solverDoubleVelocities"] for v in body) ==
                tuple(value_bits(v, 64) for v in last_step["double_values"]), "final double contact velocities differ from retained DLL capture")
    require(on.bytes + off.bytes <= LIMIT_BYTES, "ON/OFF evidence total exceeds approved cap")
    settled_interval = None
    if expected == ExpectedRun():
        require(settled_snapshot is not None and settled_costs_start is not None, "missing first original sampling endpoint")
        settled_interval = _interval_ledger(ledger, settled_snapshot)
        settled_interval["alpha"] = settled_alpha
        settled_interval["receipt_count_histogram"] = settled_receipts
        settled_interval["costs"] = dict(zip(COST_NAMES, (end - start for end, start in zip(totals, settled_costs_start))))
        require(settled_interval["included_small_steps"] == (6301 - 601) * 16, "settled interval does not include all 91200 native small steps")
    return {"schema": 1, "allChecksPassed": True, "steps": steps, "frames": frames, "whole_period_checked": True,
            "public_float_bit_comparisons": frames * 42, "small_step_cost_vector_comparisons": steps,
            "checked_mass_values": steps * 17, "window_count": windows,
            "on_sha256": on.hash.hexdigest().upper(), "off_sha256": off.hash.hexdigest().upper(),
            "on_bytes": on.bytes, "off_bytes": off.bytes, "costs": dict(zip(COST_NAMES, totals)),
            "original_gate_samples": samples, "first_original_gate_failure": first_gate,
            "alpha": {"events": events, "selected_zero": zero_alpha, "selected_nonzero": nonzero_alpha,
                      "unavailable": missing_alpha, "factor_full_rank": fullrank, "first_nonzero": first_nonzero,
                      "minimum_selected": alpha_min, "maximum_selected": alpha_max, "status_histogram": status_counts},
            "receipt_count_histogram": contact_counts, "ledger": summary,
            "settled_interval": settled_interval,
            "limits": ["Observation fidelity and ledger closure do not pass the original stability gate.",
                       "Material plus Box2D contains applied forces, gravity, contacts, cache and float integration; boundary totals are not unique root causes.",
                       "Velocity-only stages can affect following integration even with zero immediate position change.",
                       "Zero selected alpha does not remove the particular contact reaction.",
                       "All values are copied evidence, not independent re-derivation of Box2D internals or constraint J."]}


def _fixture_layout() -> dict:
    worst = (2 * HEADER_BYTES + 100816 * (8 + STEP_BYTES + 32 * MODE_BYTES) +
             100816 * (8 + COST_BYTES) + 2 * 6301 * (8 + FRAME_BYTES) +
             2 * 258 * (8 + 24 + DIAGNOSTIC_BYTES) + 2 * (8 + END_BYTES) + 2097152)
    return {"schema": 1, "header_bytes": HEADER_BYTES, "step_bytes": STEP_BYTES,
            "mode_bytes": MODE_BYTES, "frame_bytes": FRAME_BYTES, "diagnostic_bytes": DIAGNOSTIC_BYTES,
            "config_bytes": 164, "cost_bytes": COST_BYTES, "worst_case_bytes": worst,
            "input_offset": 32, "positioned_offset": 404, "damped_offset": 776,
            "uncapped_offset": 1148, "output_offset": 1520,
            "positions_offset": 0, "velocities_offset": 136, "inverse_mass_offset": 272}


def _synthetic_evidence() -> tuple[bytes, bytes, dict, ExpectedRun]:
    """Small arithmetic/transport fixture; never presented as a world trajectory."""
    expected = ExpectedRun(frames=2, substeps=2, windows=True, first_gate=None)
    layout = _fixture_layout()
    masses = [F32.unpack(F32.pack(.015 if i < 16 else .12))[0] for i in range(17)]
    initial = [F32.unpack(F32.pack(v))[0] for i in range(17) for v in (i * .012, i * .02, .001, 0.0)]
    def header(role):
        return struct.pack("<8s8IQ17f68f164s", b"DGTRACE1", 1, role, HEADER_BYTES,
                           STEP_BYTES, MODE_BYTES, FRAME_BYTES, DIAGNOSTIC_BYTES, 164,
                           LIMIT_BYTES, *masses, *initial, bytes(164))
    streams = [bytearray(header(1)), bytearray(header(0))]
    sums = [0.0] * 272
    absolute = [0.0] * 272
    totals = [0] * 12
    previous = initial[:]
    for sequence in range(1, expected.steps + 1):
        frame, sub = divmod(sequence - 1, expected.substeps)
        frame += 1
        costs = (1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0)
        meta = COST.pack(sequence, frame, sub, 6, 0, *costs)
        for i, c in enumerate(costs):
            totals[i] += c
        states = [previous[:]]
        pre = previous[:]
        for body in range(17):
            pre[body * 4 + 2] = F32.unpack(F32.pack(.003))[0]
        states.append(pre)
        integrated = pre[:]
        for body in range(17):
            integrated[body * 4] = F32.unpack(F32.pack(integrated[body * 4] + .001))[0]
        states.append(integrated)
        positioned = integrated[:]
        for body in range(17):
            positioned[body * 4] = F32.unpack(F32.pack(positioned[body * 4] - .0003))[0]
        states.append(positioned)
        damped = positioned[:]
        for body in range(17):
            damped[body * 4 + 2] = F32.unpack(F32.pack(.001))[0]
        states.append(damped)
        contact = damped[:]
        for body in range(17):
            contact[body * 4 + 2] = F32.unpack(F32.pack(.002))[0]
        states.extend([contact, contact[:], contact[:]])
        payload = bytearray(meta + struct.pack("<4I2f16i17f544f34d", 255, 0, 1, 1,
                              F32.unpack(F32.pack(F32.unpack(F32.pack(.0166666))[0] / expected.substeps))[0], 1.0,
                              128, *([0] * 15), *masses, *(v for state in states for v in state),
                              *(v for _ in range(17) for v in (.002, 0.0))) + bytes(16 * RECEIPT.size))
        payload += MODE.pack(0, 0, 63 if sequence == 1 else 7, 0 if sequence == 1 else 1,
                             1 if sequence == 1 else 0, 1, 1, 0.0, 0)
        require(len(payload) == STEP_BYTES + MODE_BYTES, "fixture packed step size")
        streams[0] += PREFIX.pack(1, len(payload)) + payload
        streams[1] += PREFIX.pack(5, COST_BYTES) + meta
        diagnostic = bytearray(DIAGNOSTIC_BYTES)
        struct.pack_into("<4i", diagnostic, 0, 6, 0, 1, 0)
        struct.pack_into("<Q", diagnostic, 24, sequence)
        for stage, name in ((2, "input"), (3, "positioned"), (4, "damped"), (5, "uncapped"), (6, "output")):
            base = layout[name + "_offset"]
            struct.pack_into("<34f", diagnostic, base, *(states[stage][body*4+a] for body in range(17) for a in range(2)))
            struct.pack_into("<34f", diagnostic, base+136, *(states[stage][body*4+2+a] for body in range(17) for a in range(2)))
            struct.pack_into("<17f", diagnostic, base+272, *(1.0 / mass for mass in masses))
        window = struct.pack("<Q4I", sequence, frame, sub, 1, DIAGNOSTIC_BYTES) + diagnostic
        streams[0] += PREFIX.pack(3, len(window)) + window
        for stage in range(8):
            before = states[stage-1] if stage else previous
            for body in range(17):
                for axis in range(2):
                    index = stage*34+body*2+axis
                    delta = states[stage][body*4+axis] - before[body*4+axis]
                    sums[index] += delta
                    absolute[index] += abs(delta)
        previous = states[7][:]
        if sub == expected.substeps - 1:
            row = [0.0] * 42
            row[32:34] = previous[64:66]
            row[34], row[40], row[41] = 1.0, -1.0, 16.0
            frame_data = struct.pack("<IIii42f", frame, 1, 0, 0, *row)
            for stream in streams:
                stream += PREFIX.pack(2, FRAME_BYTES) + frame_data
    for role, stream in ((1, streams[0]), (0, streams[1])):
        end = struct.pack("<4QiI12Q272d272d68f", expected.steps, expected.frames,
                          len(stream) + 8 + END_BYTES, expected.steps if role else 0, 0, role,
                          *totals, *(sums if role else [0.0] * 272),
                          *(absolute if role else [0.0] * 272), *previous)
        require(len(end) == END_BYTES, "fixture packed end size")
        stream += PREFIX.pack(4, END_BYTES) + end
    return bytes(streams[0]), bytes(streams[1]), layout, expected


def semantic_counterexamples() -> dict:
    on, off, layout, expected = _synthetic_evidence()
    _layout_document(layout)
    positive = review_streams(io.BytesIO(on), io.BytesIO(off), layout, expected)
    fixture_reader = TraceReader(io.BytesIO(on), 1)
    fixture_ledger = Ledger(fixture_reader.initial_units, fixture_reader.mass_units)
    interval_snapshot = None
    while fixture_ledger.steps < expected.steps:
        kind, payload = fixture_reader.next()
        if kind == 1:
            fixture_ledger.add(_parse_step(payload, fixture_ledger.steps + 1, expected, fixture_reader.mass_bits))
            if fixture_ledger.steps == expected.substeps:
                interval_snapshot = _ledger_snapshot(fixture_ledger)
    interval = _interval_ledger(fixture_ledger, interval_snapshot)
    require(interval["included_small_steps"] == expected.substeps and
            interval["core_net_dx"]["value"] == float(Fraction(fixture_ledger.previous[64] - interval_snapshot["previous"][64], GRID32)),
            "synthetic interval arithmetic failed")
    results = {}
    mutations = {}
    def changed(payload, offset, fmt, value):
        data = bytearray(payload)
        struct.pack_into(fmt, data, offset, value)
        return bytes(data)
    mutations["missing_position_stage"] = lambda k,p: changed(p,120,"<I",247) if k==1 else p
    mutations["wrong_frame"] = lambda k,p: changed(p,8,"<I",99) if k==1 else p
    mutations["wrong_substep"] = lambda k,p: changed(p,12,"<I",99) if k==1 else p
    mutations["wrong_actual_mass"] = lambda k,p: changed(p,208,"<f",.02) if k==1 else p
    mutations["position_correction_velocity_misrecorded"] = lambda k,p: changed(p,POSITION_BITS_OFFSET+(3*68+66)*4,"<f",.005) if k==1 else p
    mutations["selected_zero_erased_as_unavailable"] = lambda k,p: changed(p,STEP_BYTES+8,"<I",7) if k==1 else p
    mutations["missing_alpha_presented_as_zero_selection"] = lambda k,p: changed(p,STEP_BYTES+8,"<I",63) if k==1 else p
    mutations["wrong_mode_index"] = lambda k,p: changed(p,STEP_BYTES,"<I",1) if k==1 else p
    mutations["changed_public_float"] = lambda k,p: changed(p,16+32*4,"<I",struct.unpack_from("<I",p,16+32*4)[0]^1) if k==2 else p
    mutations["duplicate_position_correction"] = lambda k,p: changed(p,POSITION_BITS_OFFSET+(4*68+64)*4,"<f",
        2*struct.unpack_from("<f",p,POSITION_BITS_OFFSET+(3*68+64)*4)[0]-struct.unpack_from("<f",p,POSITION_BITS_OFFSET+(2*68+64)*4)[0]) if k==1 else p
    mutations["velocity_mislabeled_as_position"] = lambda k,p: changed(p,POSITION_BITS_OFFSET+(3*68+64)*4,"<I",
        struct.unpack_from("<I",p,POSITION_BITS_OFFSET+(3*68+66)*4)[0]) if k==1 else p
    mutations["continuity_gap"] = lambda k,p: changed(p,POSITION_BITS_OFFSET+64*4,"<I",
        struct.unpack_from("<I",p,POSITION_BITS_OFFSET+64*4)[0]^1) if k==1 else p
    mutations["deleted_nonzero_stage_total"] = lambda k,p: changed(p,136+(3*34+32)*8,"<d",0.0) if k==4 else p
    mutations["duplicated_stage_total"] = lambda k,p: changed(p,136+(3*34+32)*8,"<d",
        2*struct.unpack_from("<d",p,136+(3*34+32)*8)[0]) if k==4 else p
    mutations["window_inverse_mass_wrong"] = lambda k,p: changed(p,24+layout["input_offset"]+272,"<f",10.0) if k==3 else p
    mutations["missing_substep"] = lambda k,p: None if k==1 else p
    mutations["wrong_window_frame"] = lambda k,p: changed(p,8,"<I",99) if k==3 else p
    mutations["missing_fixed_window"] = lambda k,p: None if k==3 else p
    for name, mutation in mutations.items():
        triggered = False
        def patch(kind, payload, index):
            nonlocal triggered
            replacement = mutation(kind, payload) if not triggered else payload
            if replacement is None or replacement != payload:
                triggered = True
                return None if replacement is None else (kind, replacement)
            return kind, payload
        try:
            review_streams(io.BytesIO(on), io.BytesIO(off), layout, expected, patch=patch)
        except ReviewFailure as exc:
            require(triggered, f"counterexample {name} did not exercise its mutation")
            results[name] = {"rejected": True, "reason": str(exc)}
        else:
            raise ReviewFailure(f"semantic counterexample accepted: {name}")
    # Same wrong positive mass in both headers and every compact small step:
    # an internally consistent pair must still fail the independently copied
    # original diagnostic's inverse_mass, not only a single-field mismatch.
    wrong_on, wrong_off = bytearray(on), bytearray(off)
    struct.pack_into("<f", wrong_on, 48, .02)
    struct.pack_into("<f", wrong_off, 48, .02)
    def all_mass_patch(kind, payload, index):
        return kind, changed(payload,208,"<f",.02) if kind==1 else payload
    try:
        review_streams(io.BytesIO(wrong_on), io.BytesIO(wrong_off), layout, expected, patch=all_mass_patch)
    except ReviewFailure as exc:
        require("inverse_mass" in str(exc), "consistent wrong mass was not rejected by diagnostic source")
        results["consistent_wrong_mass_headers_and_steps"] = {"rejected": True, "reason": str(exc)}
    else:
        raise ReviewFailure("consistent wrong mass accepted")
    return {"scope": "Independent synthetic binary arithmetic fixture and sparse in-memory payload replacements; no world or large-file clones.",
            "positive_fixture_passed": positive["allChecksPassed"], "interval_arithmetic_passed": True,
            "fixture_on_bytes": len(on), "fixture_off_bytes": len(off),
            "fixture_on_sha256": hashlib.sha256(on).hexdigest().upper(), "fixture_off_sha256": hashlib.sha256(off).hexdigest().upper(),
            "transport": "Payload lengths and TLV structure remain valid for field mutations; no checksum-only rejection is used.",
            "negative_count": len(results), "negatives": results}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--on", type=Path)
    parser.add_argument("--off", type=Path)
    parser.add_argument("--layout", type=Path)
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        _arithmetic_self_test()
        negatives = semantic_counterexamples()
        print(json.dumps({"arithmetic_passed": True, "semantic_counterexamples": negatives}, allow_nan=False))
        return 0
    require(all((args.on, args.off, args.layout, args.reference, args.output)),
            "--on --off --layout --reference --output are required for actual fixed-prefix review")
    layout = _layout_document(json.loads(args.layout.read_text(encoding="utf-8-sig")))
    reference = json.loads(args.reference.read_text(encoding="utf-8-sig"))
    with args.on.open("rb") as on, args.off.open("rb") as off:
        result = review_streams(on, off, layout, reference=reference)
    result["semantic_counterexamples"] = semantic_counterexamples()
    require(args.on.stat().st_size + args.off.stat().st_size + len(json.dumps(result)) <= LIMIT_BYTES,
            "review evidence exceeds approved total cap")
    with args.output.open("x", encoding="utf-8") as destination:
        json.dump(result, destination, indent=2, allow_nan=False)
        destination.write("\n")
    print(json.dumps({"allChecksPassed": result["allChecksPassed"], "steps": result["steps"], "frames": result["frames"],
                      "first_original_gate_failure": result["first_original_gate_failure"], "alpha": result["alpha"],
                      "review": str(args.output)}, allow_nan=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ReviewFailure, OSError, json.JSONDecodeError, struct.error) as error:
        print(f"REJECTED: {error}", file=sys.stderr)
        raise SystemExit(1)

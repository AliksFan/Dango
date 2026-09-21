"""Render a local performance evidence chart (optional installed matplotlib).

This only plots measured samples; it does not evaluate or change any gate.
"""
import argparse
import json
from pathlib import Path

from analyze_samples import frame_count, schema_of, validate_sample

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("samples", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.samples.resolve() == args.output.resolve():
        parser.error("input and output must differ")
    rows = [json.loads(line) for line in args.samples.read_text(encoding="utf-8-sig").splitlines()]
    if not rows or any(validate_sample(row) for row in rows) or len({schema_of(row) for row in rows}) != 1:
        parser.error("samples must have one supported schema and valid counters")
    schema = schema_of(rows[0])
    x = [r["elapsedSeconds"] / 60 for r in rows]
    fig, axes = plt.subplots(4, 1, figsize=(13, 10), sharex=True, constrained_layout=True)
    fig.suptitle("Dango | isolated Product host | real elapsed time", fontsize=15, weight="bold")
    for ax in axes:
        ax.grid(axis="y", alpha=.18)
        ax.spines[["top", "right"]].set_visible(False)
        for row in rows:
            color = "#b5a4e0" if row["state"] == "doze" else "#808d9e" if row["state"] in ("hidden", "disposed") else None
            if color:
                ax.axvspan((row["elapsedSeconds"] - row["intervalSeconds"]) / 60,
                           row["elapsedSeconds"] / 60, color=color, alpha=.16, linewidth=0)
    axes[0].plot(x, [r["cpuMachinePercent"] for r in rows], color="#1d70a2", linewidth=.7)
    axes[0].axhline(1, color="#b44839", linestyle="--", label="Idle limit: 1%")
    axes[0].set_ylabel("CPU (% of machine)")
    axes[0].legend(loc="upper right")
    axes[1].plot(x, [r["workingSetBytes"] / 1e6 for r in rows], color="#167d69", label="Working set")
    axes[1].plot(x, [r["privateBytes"] / 1e6 for r in rows], color="#7b8f8b", linewidth=.8, label="Private bytes")
    axes[1].axhline(250, color="#b44839", linestyle="--", label="Idle working-set limit: 250 MB")
    axes[1].set_ylabel("Memory (decimal MB)")
    axes[1].legend(loc="lower right", ncol=3)
    axes[2].plot(x, [frame_count(r) / r["intervalSeconds"] for r in rows], color="#7b4ea3", linewidth=.7)
    axes[2].axhline(60, color="#b44839", linestyle="--", label="Active update target: 60")
    axes[2].set_ylabel("Applied frames / second" if schema == 2 else "Timer completions / second")
    axes[2].legend(loc="upper right")
    axes[3].plot(x, [r["handles"] for r in rows], color="#1d70a2", label="Handles")
    axes[3].set_ylabel("Handles")
    threads = axes[3].twinx()
    threads.plot(x, [r["threads"] for r in rows], color="#bf6e2c", linewidth=.7, label="Threads")
    threads.set_ylabel("Threads")
    threads.spines["top"].set_visible(False)
    axes[3].legend(loc="upper left")
    threads.legend(loc="upper right")
    axes[3].set_xlabel("Minutes from probe startup | purple bands: app doze | grey bands: hidden/disposed")
    fig.text(.005, -.018, "CPU/memory include observer overhead. Update rate is not GPU present FPS. Limits apply to the indicated state.", fontsize=9)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(args.output, dpi=160, bbox_inches="tight")
    plt.close(fig)


if __name__ == "__main__":
    main()

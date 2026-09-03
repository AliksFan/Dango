#!/usr/bin/env python3
"""Validate the AP-005 authored fallback pack and file references."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path, PurePosixPath


EXPECTED_POSES = {
    "idle": ["rest-start", "inhale", "rest-return", "blink", "rest-end"],
    "wander": ["rest", "lean", "compress", "glide", "settle"],
    "approach-island": ["travel", "slow", "lower", "align"],
    "climb-up": ["approach", "grab", "transfer", "cross", "stand"],
    "on-island": ["rest-start", "breathe", "rest-end"],
    "climb-down": ["stand", "peer", "edge-contact", "release", "fall"],
    "dragged": ["grabbed", "suspend", "follow"],
    "falling": ["fall-start", "stretch", "pre-contact"],
    "landing": ["fall", "contact", "compress", "rebound", "settle"],
    "reacting": ["rest", "compress", "lift", "settle"],
}
EXPECTED_ACTIONS = list(EXPECTED_POSES)
LOOPING_ACTIONS = {"idle", "wander", "on-island", "dragged"}
EXPECTED_EVENTS = {
    "landing": ("landed", "contact", "ground-contact"),
    "climb-up": ("island-grabbed", "grab", "island-grab"),
    "climb-down": ("island-released", "release", "island-grab"),
}
EXPECTED_FINAL_ANCHORS = {
    "climb-up": "island-stand",
    "climb-down": "ground-contact",
    "landing": "ground-contact",
}


def is_number(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def valid_point(value: object, width: int, height: int) -> bool:
    return (
        isinstance(value, list)
        and len(value) == 2
        and all(is_number(component) for component in value)
        and 0 <= value[0] <= width
        and 0 <= value[1] <= height
    )


def safe_existing_path(value: object, root: Path) -> bool:
    if not isinstance(value, str) or not value or "\\" in value:
        return False
    relative = PurePosixPath(value)
    if relative.is_absolute() or ".." in relative.parts:
        return False
    target = (root / Path(*relative.parts)).resolve()
    try:
        target.relative_to(root.resolve())
    except ValueError:
        return False
    return target.is_file()


def validate_document(document: object, root: Path) -> list[str]:
    errors: list[str] = []
    if not isinstance(document, dict):
        return ["document must be an object"]
    if document.get("version") != 1:
        errors.append("version must equal 1")
    if document.get("status") != "candidate":
        errors.append("status must equal candidate")
    if document.get("layers") != ["body", "eyes", "hat"]:
        errors.append("layers must equal body, eyes, hat")
    if document.get("forbiddenLayers") != ["mouth", "blush", "shadow"]:
        errors.append("forbiddenLayers must equal mouth, blush, shadow")

    canvas = document.get("canvas")
    if not isinstance(canvas, dict) or canvas.get("width") != 160 or canvas.get("height") != 120:
        errors.append("canvas must equal 160 by 120")
    width, height = 160, 120

    actions = document.get("actions")
    if not isinstance(actions, list):
        return errors + ["actions must be an array"]
    action_ids = [action.get("id") for action in actions if isinstance(action, dict)]
    if action_ids != EXPECTED_ACTIONS:
        missing = [action_id for action_id in EXPECTED_ACTIONS if action_id not in action_ids]
        unexpected = [action_id for action_id in action_ids if action_id not in EXPECTED_ACTIONS]
        errors.append(
            "actions must contain all visible Core actions in order"
            f"; missing={missing}; unexpected={unexpected}"
        )

    for action in actions:
        if not isinstance(action, dict):
            errors.append("each action must be an object")
            continue
        action_id = action.get("id")
        duration = action.get("durationMilliseconds")
        if action_id not in EXPECTED_POSES:
            continue
        if not isinstance(duration, int) or duration <= 0:
            errors.append(f"{action_id}: durationMilliseconds must be a positive integer")
            continue
        if action.get("loop") is not (action_id in LOOPING_ACTIONS):
            errors.append(f"{action_id}: loop flag is incorrect")

        poses = action.get("poses")
        if not isinstance(poses, list):
            errors.append(f"{action_id}: poses must be an array")
            continue
        pose_ids = [pose.get("id") for pose in poses if isinstance(pose, dict)]
        if pose_ids != EXPECTED_POSES[action_id]:
            errors.append(f"{action_id}: pose IDs do not match the approved sequence")

        previous_time = -1
        for pose in poses:
            if not isinstance(pose, dict):
                errors.append(f"{action_id}: each pose must be an object")
                continue
            pose_id = pose.get("id", "unknown")
            time = pose.get("timeMilliseconds")
            if not isinstance(time, int) or time < 0 or time > duration:
                errors.append(f"{action_id}/{pose_id}: time is outside clip duration")
            elif time <= previous_time:
                errors.append(f"{action_id}: pose times must be strictly increasing")
            else:
                previous_time = time
            for layer in ("body", "eyes", "hat"):
                if not safe_existing_path(pose.get(layer), root):
                    errors.append(f"{action_id}/{pose_id}: invalid or missing {layer} file")
            for field in ("eyesTranslation", "hatTranslation", "hatAnchor"):
                if not valid_point(pose.get(field), width, height) and field == "hatAnchor":
                    errors.append(f"{action_id}/{pose_id}: invalid {field}")
                elif field != "hatAnchor":
                    value = pose.get(field)
                    if not isinstance(value, list) or len(value) != 2 or not all(is_number(item) for item in value):
                        errors.append(f"{action_id}/{pose_id}: invalid {field}")

        interruptible = action.get("interruptiblePoseIds")
        if not isinstance(interruptible, list) or not set(interruptible).issubset(set(pose_ids)):
            errors.append(f"{action_id}: interruptiblePoseIds must reference poses")
        if not isinstance(action.get("reducedMotion"), str) or not action["reducedMotion"].strip():
            errors.append(f"{action_id}: reducedMotion is required")

        if action_id in EXPECTED_EVENTS:
            expected_name, expected_pose, expected_anchor = EXPECTED_EVENTS[action_id]
            events = action.get("events")
            matching = [
                event
                for event in events if isinstance(event, dict)
            ] if isinstance(events, list) else []
            if len(matching) != 1 or (
                matching[0].get("name"),
                matching[0].get("poseId"),
                matching[0].get("anchor"),
            ) != (expected_name, expected_pose, expected_anchor):
                errors.append(f"{action_id}: required {expected_name} contact event is missing or incorrect")
            elif not valid_point(matching[0].get("point"), width, height):
                errors.append(f"{action_id}: event point is invalid")

        if action_id in EXPECTED_FINAL_ANCHORS:
            final_anchor = action.get("finalAnchor")
            if not isinstance(final_anchor, dict) or final_anchor.get("name") != EXPECTED_FINAL_ANCHORS[action_id]:
                errors.append(f"{action_id}: required final anchor is missing or incorrect")
            elif not valid_point(final_anchor.get("point"), width, height):
                errors.append(f"{action_id}: final anchor point is invalid")

    return errors


def provenance_paths(path: Path) -> tuple[set[str], list[str]]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        return set(), [f"{path.name}: provenance cannot be read: {exc}"]
    files = document.get("files") if isinstance(document, dict) else None
    if not isinstance(files, list):
        return set(), [f"{path.name}: provenance files must be an array"]
    values = {entry.get("path") for entry in files if isinstance(entry, dict)}
    return {value for value in values if isinstance(value, str)}, []


def validate_source_assets(document: object, root: Path) -> list[str]:
    if not isinstance(document, dict) or not isinstance(document.get("actions"), list):
        return []
    errors: list[str] = []
    source_entries, source_errors = provenance_paths(root / "provenance.json")
    export_entries, export_errors = provenance_paths(root / "export" / "provenance.json")
    errors.extend(source_errors)
    errors.extend(export_errors)
    required_sources = {"motion-samples.json", "export-motion-samples.ps1", "build_ap005_source_pack.py"}
    required_exports: set[str] = set()

    for action in document["actions"]:
        if not isinstance(action, dict) or not isinstance(action.get("poses"), list):
            continue
        for pose in action["poses"]:
            if not isinstance(pose, dict):
                continue
            for layer in ("body", "eyes", "hat"):
                value = pose.get(layer)
                if isinstance(value, str) and value.startswith("export/"):
                    required_exports.add(value.removeprefix("export/"))
            body = pose.get("body")
            if not isinstance(body, str) or not body.startswith("export/") or not body.endswith(".png"):
                continue
            source_relative = "source/" + body.removeprefix("export/").removesuffix(".png") + ".svg"
            required_sources.add(source_relative)
            source_path = root / Path(*PurePosixPath(source_relative).parts)
            try:
                svg = source_path.read_text(encoding="utf-8")
            except (OSError, UnicodeError) as exc:
                errors.append(f"{source_relative}: source SVG cannot be read: {exc}")
                continue
            lowered = svg.lower()
            for forbidden in ("lineargradient", "radialgradient", "<filter", "opacity", "<style", "mouth", "blush", "shadow"):
                if forbidden in lowered:
                    errors.append(f"{source_relative}: forbidden SVG feature {forbidden}")
            if svg.count("<path ") != 1 or re.search(r"<(ellipse|circle|rect|line|polyline|polygon)\b", svg, re.IGNORECASE):
                errors.append(f"{source_relative}: body must be one continuous path without limb primitives")
            if len(re.findall(r'fill="#[0-9A-Fa-f]{6}"', svg)) != 1:
                errors.append(f"{source_relative}: body must have exactly one solid hex fill")

    for missing in sorted(required_sources - source_entries):
        errors.append(f"source provenance is missing {missing}")
    for missing in sorted(required_exports - export_entries):
        errors.append(f"export provenance is missing {missing}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()
    try:
        document = json.loads(args.manifest.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        print(json.dumps({"errors": [f"manifest cannot be read: {exc}"]}, ensure_ascii=False, indent=2))
        return 1
    errors = validate_document(document, args.manifest.parent)
    errors.extend(validate_source_assets(document, args.manifest.parent))
    action_count = len(document.get("actions", [])) if isinstance(document, dict) else 0
    print(json.dumps({"actions": action_count, "errors": errors}, ensure_ascii=False, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    sys.exit(main())

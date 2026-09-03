#!/usr/bin/env python3
"""Build deterministic AP-005 vector sources, manifests, and review layout."""

from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent
PACKAGE_ROOT = ROOT.parent
BODY_FILL = "#FFE8C2"
EYES = "export/shared/eyes-open.png"
HAT = "export/shared/hat-sprout.png"

# id, duration, loop, pose ids/times, interruptible poses, reduced-motion note
ACTIONS = [
    ("idle", 2400, True, [("rest-start", 0), ("inhale", 1000), ("rest-return", 1800), ("blink", 2100), ("rest-end", 2200)], ["rest-start", "rest-return", "rest-end"], "Keep low-frequency contour breathing and blinking; remove secondary sway."),
    ("wander", 960, True, [("rest", 0), ("lean", 180), ("compress", 360), ("glide", 620), ("settle", 900)], ["rest", "settle"], "Use rest, one small weight shift, and settle without continuous soft-body stepping."),
    ("approach-island", 640, False, [("travel", 0), ("slow", 260), ("lower", 440), ("align", 620)], ["travel", "align"], "Keep travel, lowering, and final alignment with reduced deformation."),
    ("climb-up", 720, False, [("approach", 0), ("grab", 180), ("transfer", 320), ("cross", 500), ("stand", 720)], ["approach", "stand"], "Preserve grab contact, transfer, and final stance with reduced deformation."),
    ("on-island", 2200, True, [("rest-start", 0), ("breathe", 1000), ("rest-end", 2100)], ["rest-start", "rest-end"], "Hold the island stance and use one subtle low-frequency breath."),
    ("climb-down", 680, False, [("stand", 0), ("peer", 150), ("edge-contact", 300), ("release", 460), ("fall", 640)], ["stand", "fall"], "Preserve edge contact, release, and the transition to falling with less deformation."),
    ("dragged", 480, True, [("grabbed", 0), ("suspend", 160), ("follow", 420)], ["grabbed", "follow"], "Keep the grabbed and follow silhouettes; omit continuous spring lag."),
    ("falling", 360, False, [("fall-start", 0), ("stretch", 160), ("pre-contact", 330)], ["pre-contact"], "Keep fall start and pre-contact with a smaller stretch."),
    ("landing", 420, False, [("fall", 0), ("contact", 120), ("compress", 180), ("rebound", 280), ("settle", 420)], ["settle"], "Keep first contact, landed, and settle while reducing compression and rebound."),
    ("reacting", 520, False, [("rest", 0), ("compress", 120), ("lift", 260), ("settle", 480)], ["rest", "settle"], "Keep one restrained compress-and-lift response before settling."),
]

EXISTING = {"idle", "climb-up", "landing"}

# left, right, top, bottom, top-center; every silhouette is a single continuous rounded body.
SHAPES = {
    "wander": [(29, 133, 17, 114, 82), (32, 136, 19, 114, 88), (25, 137, 28, 114, 87), (35, 141, 20, 112, 94), (30, 134, 17, 114, 83)],
    "approach-island": [(36, 140, 20, 114, 94), (39, 139, 23, 114, 96), (42, 137, 29, 114, 95), (44, 136, 27, 114, 94)],
    "on-island": [(35, 125, 24, 108, 80), (37, 124, 20, 108, 80), (35, 125, 24, 108, 80)],
    "climb-down": [(35, 125, 24, 108, 80), (39, 131, 25, 109, 88), (43, 137, 29, 110, 94), (46, 136, 23, 105, 94), (50, 130, 12, 101, 91)],
    "dragged": [(35, 125, 18, 105, 80), (40, 120, 9, 100, 80), (43, 129, 13, 104, 88)],
    "falling": [(42, 118, 8, 100, 80), (47, 113, 3, 106, 80), (39, 121, 13, 110, 80)],
    "reacting": [(29, 132, 17, 114, 80), (23, 137, 34, 114, 80), (37, 123, 8, 106, 80), (30, 132, 18, 114, 81)],
}

TRANSLATIONS = {
    "wander": [(0, 0), (5, 1), (3, 9), (11, 2), (1, 0)],
    "approach-island": [(8, 1), (9, 3), (8, 9), (8, 7)],
    "on-island": [(0, 0), (0, -2), (0, 0)],
    "climb-down": [(0, 0), (6, 2), (10, 8), (9, -2), (7, -8)],
    "dragged": [(0, -4), (0, -10), (7, -7)],
    "falling": [(0, -7), (0, -12), (0, -3)],
    "reacting": [(0, 0), (0, 10), (0, -8), (0, 0)],
}


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value.replace("\r\n", "\n"), encoding="utf-8", newline="\n")


def body_svg(title: str, shape: tuple[int, int, int, int, int]) -> str:
    left, right, top, bottom, peak = shape
    width = right - left
    shoulder = top + 16
    lower = bottom - 18
    path = (
        f"M{peak} {top}"
        f"C{right - width // 5} {top},{right} {shoulder},{right} {70 + (top - 16) // 3}"
        f"C{right} {lower},{right - width // 5} {bottom},{(left + right) // 2} {bottom}"
        f"C{left + width // 5} {bottom},{left} {lower},{left} {70 + (top - 16) // 3}"
        f"C{left} {shoulder},{left + width // 5} {top},{peak} {top}Z"
    )
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 120" width="160" height="120">\n'
        f"  <title>{title}</title>\n"
        f'  <path d="{path}" fill="{BODY_FILL}" />\n'
        "</svg>\n"
    )


def pose_path(action_id: str, pose_id: str) -> str:
    return f"export/{action_id}/body-{pose_id}.png"


def build_sources() -> None:
    for action_id, shapes in SHAPES.items():
        pose_ids = next(poses for current, _, _, poses, _, _ in ACTIONS if current == action_id)
        for (pose_id, _), shape in zip(pose_ids, shapes, strict=True):
            write_text(
                ROOT / "source" / action_id / f"body-{pose_id}.svg",
                body_svg(f"{action_id} {pose_id} body", shape),
            )


def existing_pose(action_id: str, pose_id: str) -> dict[str, object]:
    body_names = {
        ("idle", "rest-start"): "body-rest.png", ("idle", "inhale"): "body-inhale.png",
        ("idle", "rest-return"): "body-rest.png", ("idle", "blink"): "body-rest.png", ("idle", "rest-end"): "body-rest.png",
        ("landing", "fall"): "body-fall.png", ("landing", "contact"): "body-contact.png", ("landing", "compress"): "body-compress.png",
        ("landing", "rebound"): "body-rebound.png", ("landing", "settle"): "body-settle.png",
        ("climb-up", "approach"): "body-approach.png", ("climb-up", "grab"): "body-grab.png", ("climb-up", "transfer"): "body-transfer.png",
        ("climb-up", "cross"): "body-cross.png", ("climb-up", "stand"): "body-stand.png",
    }
    return {"body": f"export/{action_id}/{body_names[(action_id, pose_id)]}", "eyesTranslation": [0, 0], "hatTranslation": [0, 0], "hatAnchor": [80, 16]}


def build_motion_manifest() -> dict[str, object]:
    actions = []
    for action_id, duration, loop, poses, interruptible, reduced in ACTIONS:
        rendered = []
        for index, (pose_id, time_ms) in enumerate(poses):
            details = existing_pose(action_id, pose_id) if action_id in EXISTING else {
                "body": pose_path(action_id, pose_id),
                "eyesTranslation": TRANSLATIONS[action_id][index],
                "hatTranslation": [TRANSLATIONS[action_id][index][0], TRANSLATIONS[action_id][index][1] - 2],
                "hatAnchor": [80 + TRANSLATIONS[action_id][index][0], 16 + TRANSLATIONS[action_id][index][1] - 2],
            }
            if action_id == "idle" and pose_id == "inhale": details.update({"eyesTranslation": [0, -2], "hatTranslation": [0, -4], "hatAnchor": [80, 12]})
            if action_id == "landing":
                landing = {"fall": ([0, -6], [0, -4], [80, 12]), "contact": ([0, 2], [0, 4], [80, 20]), "compress": ([0, 16], [0, 32], [80, 48]), "rebound": ([0, -4], [0, -6], [80, 10]), "settle": ([0, 0], [0, 0], [80, 16])}[pose_id]
                details.update({"eyesTranslation": landing[0], "hatTranslation": landing[1], "hatAnchor": landing[2]})
            rendered.append({"id": pose_id, "timeMilliseconds": time_ms, **details, "eyes": "export/shared/eyes-closed.png" if action_id == "idle" and pose_id == "blink" else EYES, "hat": HAT})
        action: dict[str, object] = {"id": action_id, "durationMilliseconds": duration, "loop": loop, "poses": rendered, "interruptiblePoseIds": interruptible, "reducedMotion": reduced}
        if action_id == "climb-up":
            action.update({"events": [{"name": "island-grabbed", "poseId": "grab", "anchor": "island-grab", "point": [120, 58]}], "finalAnchor": {"name": "island-stand", "point": [80, 108]}})
        elif action_id == "climb-down":
            action.update({"events": [{"name": "island-released", "poseId": "release", "anchor": "island-grab", "point": [120, 58]}], "finalAnchor": {"name": "ground-contact", "point": [80, 114]}})
        elif action_id == "landing":
            action.update({"events": [{"name": "landed", "poseId": "contact", "anchor": "ground-contact", "point": [80, 114]}], "finalAnchor": {"name": "ground-contact", "point": [80, 114]}})
        actions.append(action)
    return {"version": 1, "status": "candidate", "canvas": {"width": 160, "height": 120}, "layers": ["body", "eyes", "hat"], "forbiddenLayers": ["mouth", "blush", "shadow"], "actions": actions}


def layer_id(action_id: str, pose_id: str) -> str:
    return f"body-{action_id}-{pose_id}"


def build_runtime_candidate(manifest: dict[str, object]) -> dict[str, object]:
    layers: list[dict[str, object]] = []
    clips: list[dict[str, object]] = []
    action_map: dict[str, str] = {}
    state_names = {"idle": "Idle", "wander": "Wander", "approach-island": "ApproachIsland", "climb-up": "ClimbUp", "on-island": "OnIsland", "climb-down": "ClimbDown", "dragged": "Dragged", "falling": "Falling", "landing": "Landing", "reacting": "Reacting"}
    seen_layers: set[str] = set()
    for action in manifest["actions"]:
        frames = []
        eyes_x, eyes_y, hat_x, hat_y = [], [], [], []
        for pose in action["poses"]:
            body_id = layer_id(action["id"], pose["id"])
            if body_id not in seen_layers:
                layers.append({"id": body_id, "order": 0, "source": f"motion-samples/{pose['body']}", "slot": "body"})
                seen_layers.add(body_id)
            eye_id = "eyes-closed" if pose["eyes"].endswith("eyes-closed.png") else "eyes-open"
            frames.append({"timeMilliseconds": pose["timeMilliseconds"], "layers": [body_id, eye_id, "hat-sprout"]})
            for target, values in ((eyes_x, pose["eyesTranslation"][0]), (eyes_y, pose["eyesTranslation"][1]), (hat_x, pose["hatTranslation"][0]), (hat_y, pose["hatTranslation"][1])):
                target.append({"timeMilliseconds": pose["timeMilliseconds"], "value": values})
        clips.append({"id": action["id"], "durationMilliseconds": action["durationMilliseconds"], "loop": action["loop"], "frames": frames, "transformTracks": [
            {"targetLayer": "eyes-open", "property": "TranslationX", "keyframes": eyes_x},
            {"targetLayer": "eyes-open", "property": "TranslationY", "keyframes": eyes_y},
            {"targetLayer": "hat-sprout", "property": "TranslationX", "keyframes": hat_x},
            {"targetLayer": "hat-sprout", "property": "TranslationY", "keyframes": hat_y},
        ]})
        action_map[state_names[action["id"]]] = action["id"]
    layers.extend([
        {"id": "eyes-open", "order": 1, "source": "motion-samples/export/shared/eyes-open.png", "slot": "face"},
        {"id": "eyes-closed", "order": 1, "source": "motion-samples/export/shared/eyes-closed.png", "slot": "face"},
        {"id": "hat-sprout", "order": 2, "source": "motion-samples/export/shared/hat-sprout.png", "slot": "accessory"},
    ])
    return {
        "$schema": "../../placeholder/phase-1.asset.schema.json", "assetVersion": 1,
        "compatibility": {"minimumRuntimeVersion": 1, "maximumRuntimeVersion": 1},
        "canvas": {"width": 160, "height": 120, "pixelDensity": 1, "origin": {"x": 0, "y": 0}},
        "layers": layers,
        "slots": [{"id": "body", "x": 80, "y": 60}, {"id": "face", "x": 80, "y": 48}, {"id": "accessory", "x": 80, "y": 16}],
        "clips": clips, "actions": action_map,
        "anchors": [{"id": "ground-contact", "x": 80, "y": 114}, {"id": "click", "x": 80, "y": 52}, {"id": "island-grab", "x": 120, "y": 58}, {"id": "island-stand", "x": 80, "y": 108}],
        "hitShapes": [
            {"id": "click", "purpose": "Click", "kind": "Ellipse", "bounds": {"x": 20, "y": 10, "width": 120, "height": 104}},
            {"id": "drag", "purpose": "Drag", "kind": "Ellipse", "bounds": {"x": 16, "y": 8, "width": 128, "height": 108}},
            {"id": "collision", "purpose": "Collision", "kind": "Rectangle", "bounds": {"x": 28, "y": 18, "width": 104, "height": 96}},
        ],
        "events": [{"clip": "climb-up", "timeMilliseconds": 180, "name": "island-grabbed", "anchor": "island-grab"}, {"clip": "climb-down", "timeMilliseconds": 460, "name": "island-released", "anchor": "island-grab"}, {"clip": "landing", "timeMilliseconds": 120, "name": "landed", "anchor": "ground-contact"}],
        "fallbacks": [{"from": action_id, "to": "idle"} for action_id, *_ in ACTIONS if action_id != "idle"],
    }


def build_review_html(manifest: dict[str, object]) -> str:
    sections = []
    for action in manifest["actions"]:
        cards = []
        for pose in action["poses"]:
            cards.append(f'<figure><div class="stage"><img src="../{pose["body"]}"><img src="../{pose["eyes"]}" style="transform:translate({pose["eyesTranslation"][0]}px,{pose["eyesTranslation"][1]}px)"><img src="../{pose["hat"]}" style="transform:translate({pose["hatTranslation"][0]}px,{pose["hatTranslation"][1]}px)"></div><figcaption>{pose["id"]}<small>{pose["timeMilliseconds"]} ms</small></figcaption></figure>')
        sections.append(f'<section><h2>{action["id"]}</h2><div class="poses">{"".join(cards)}</div></section>')
    return f'''<!doctype html><html><head><meta charset="utf-8"><title>AP-005 authored fallback pack</title><style>
body{{margin:0;padding:28px;background:#f5f1e9;color:#302b27;font:14px "Segoe UI",sans-serif}}h1{{margin:0 0 4px}}p{{margin:0 0 24px;color:#736b64}}section{{margin:18px 0}}h2{{font-size:16px;margin:0 0 8px}}.poses{{display:flex;gap:8px;flex-wrap:wrap}}figure{{margin:0;background:white;border-radius:12px;padding:6px}}.stage{{position:relative;width:160px;height:120px;background:#faf8f4;border-radius:8px;overflow:hidden}}img{{position:absolute;inset:0;width:160px;height:120px}}figcaption{{display:flex;justify-content:space-between;padding:5px 3px 1px}}small{{color:#8b827a}}
</style></head><body><h1>AP-005 手工关键姿势 / fallback 候选包</h1><p>静态 A5 预审材料；不是 WPF 播放、Box2D 物理或桌面体验完成证据。</p>{''.join(sections)}</body></html>'''


def main() -> None:
    build_sources()
    manifest = build_motion_manifest()
    write_text(ROOT / "motion-samples.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    write_text(PACKAGE_ROOT / "candidate.asset.json", json.dumps(build_runtime_candidate(manifest), ensure_ascii=False, indent=2) + "\n")
    write_text(ROOT / "review" / "phase-1-authored-fallback-pack.html", build_review_html(manifest) + "\n")
    print("Built AP-005 source pack: 10 actions, 27 new SVG poses, runtime candidate, and review layout")


if __name__ == "__main__":
    main()

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_motion_samples import EXPECTED_POSES, LOOPING_ACTIONS, validate_document, validate_source_assets


def pose(root: Path, pose_id: str, index: int) -> dict[str, object]:
    return {
        "id": pose_id,
        "timeMilliseconds": index * 10,
        "body": "idle.png",
        "eyes": "open.png",
        "hat": "hat.png",
        "eyesTranslation": [0, 0],
        "hatTranslation": [0, 0],
        "hatAnchor": [80, 16],
    }


def valid_document(root: Path) -> dict[str, object]:
    for relative in ("idle.png", "open.png", "hat.png"):
        (root / relative).write_bytes(b"fixture")
    document = {
        "version": 1,
        "status": "candidate",
        "canvas": {"width": 160, "height": 120},
        "layers": ["body", "eyes", "hat"],
        "forbiddenLayers": ["mouth", "blush", "shadow"],
        "actions": [],
    }

    for action_id, pose_ids in EXPECTED_POSES.items():
        action = {
            "id": action_id,
            "durationMilliseconds": 100,
            "loop": action_id in LOOPING_ACTIONS,
            "poses": [pose(root, pose_id, index) for index, pose_id in enumerate(pose_ids)],
            "interruptiblePoseIds": [pose_ids[-1]],
            "reducedMotion": "fixture",
        }
        if action_id == "landing":
            action["events"] = [{"name": "landed", "poseId": "contact", "anchor": "ground-contact", "point": [80, 114]}]
            action["finalAnchor"] = {"name": "ground-contact", "point": [80, 114]}
        elif action_id == "climb-up":
            action["events"] = [{"name": "island-grabbed", "poseId": "grab", "anchor": "island-grab", "point": [120, 58]}]
            action["finalAnchor"] = {"name": "island-stand", "point": [80, 114]}
        elif action_id == "climb-down":
            action["events"] = [{"name": "island-released", "poseId": "release", "anchor": "island-grab", "point": [120, 58]}]
            action["finalAnchor"] = {"name": "ground-contact", "point": [80, 114]}
        document["actions"].append(action)
    return document


class ValidateMotionSamplesTests(unittest.TestCase):
    def test_valid_complete_action_pack_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.assertEqual([], validate_document(valid_document(root), root))

    def test_legacy_three_action_sample_fails_with_missing_visible_actions(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            document = valid_document(root)
            document["actions"] = [
                action for action in document["actions"] if action["id"] in {"idle", "landing", "climb-up"}
            ]
            errors = validate_document(document, root)
            self.assertTrue(any("wander" in error and "reacting" in error for error in errors))

    def test_unordered_pose_times_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            document = valid_document(root)
            document["actions"][0]["poses"][1]["timeMilliseconds"] = 0
            errors = validate_document(document, root)
            self.assertTrue(any("strictly increasing" in error for error in errors))

    def test_missing_contact_event_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            document = valid_document(root)
            landing = next(action for action in document["actions"] if action["id"] == "landing")
            landing["events"] = []
            errors = validate_document(document, root)
            self.assertTrue(any("landed" in error for error in errors))

    def test_forbidden_layer_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            document = valid_document(root)
            document["layers"].append("mouth")
            errors = validate_document(document, root)
            self.assertTrue(any("layers must equal" in error for error in errors))

    def test_gradient_in_body_source_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source" / "idle" / "body-rest.svg"
            source.parent.mkdir(parents=True)
            source.write_text(
                '<svg><linearGradient id="g"/><path d="M0 0Z" fill="#FFE8C2" /></svg>',
                encoding="utf-8",
            )
            (root / "export").mkdir()
            (root / "provenance.json").write_text(
                '{"files":[{"path":"motion-samples.json"},{"path":"export-motion-samples.ps1"},'
                '{"path":"build_ap005_source_pack.py"},{"path":"source/idle/body-rest.svg"}]}',
                encoding="utf-8",
            )
            (root / "export" / "provenance.json").write_text(
                '{"files":[{"path":"idle/body-rest.png"},{"path":"shared/eyes-open.png"},'
                '{"path":"shared/hat-sprout.png"}]}',
                encoding="utf-8",
            )
            document = {"actions": [{"id": "idle", "poses": [{
                "body": "export/idle/body-rest.png",
                "eyes": "export/shared/eyes-open.png",
                "hat": "export/shared/hat-sprout.png",
            }]}]}

            errors = validate_source_assets(document, root)

            self.assertTrue(any("lineargradient" in error for error in errors))


if __name__ == "__main__":
    unittest.main()

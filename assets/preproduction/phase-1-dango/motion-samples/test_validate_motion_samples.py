from __future__ import annotations

import copy
import tempfile
import unittest
from pathlib import Path

from validate_motion_samples import validate_document


def valid_document(root: Path) -> dict[str, object]:
    for relative in ("idle.png", "open.png", "hat.png"):
        (root / relative).write_bytes(b"fixture")
    return {
        "version": 1,
        "status": "candidate",
        "canvas": {"width": 160, "height": 120},
        "layers": ["body", "eyes", "hat"],
        "forbiddenLayers": ["mouth", "blush", "shadow"],
        "actions": [
            {
                "id": "idle",
                "durationMilliseconds": 100,
                "loop": True,
                "poses": [
                    {
                        "id": "rest-start",
                        "timeMilliseconds": 0,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 16],
                    },
                    {
                        "id": "inhale",
                        "timeMilliseconds": 50,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 12],
                    },
                    {
                        "id": "rest-return",
                        "timeMilliseconds": 70,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 16],
                    },
                    {
                        "id": "blink",
                        "timeMilliseconds": 80,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 16],
                    },
                    {
                        "id": "rest-end",
                        "timeMilliseconds": 90,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 16],
                    },
                ],
                "interruptiblePoseIds": ["rest-start", "rest-return", "rest-end"],
                "reducedMotion": "fixture",
            },
            {
                "id": "landing",
                "durationMilliseconds": 100,
                "loop": False,
                "poses": [
                    {
                        "id": pose_id,
                        "timeMilliseconds": index * 20,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 16],
                    }
                    for index, pose_id in enumerate(("fall", "contact", "compress", "rebound", "settle"))
                ],
                "events": [{"name": "landed", "poseId": "contact", "anchor": "ground-contact", "point": [80, 114]}],
                "interruptiblePoseIds": ["settle"],
                "reducedMotion": "fixture",
            },
            {
                "id": "climb-up",
                "durationMilliseconds": 100,
                "loop": False,
                "poses": [
                    {
                        "id": pose_id,
                        "timeMilliseconds": index * 20,
                        "body": "idle.png",
                        "eyes": "open.png",
                        "hat": "hat.png",
                        "eyesTranslation": [0, 0],
                        "hatTranslation": [0, 0],
                        "hatAnchor": [80, 16],
                    }
                    for index, pose_id in enumerate(("approach", "grab", "transfer", "cross", "stand"))
                ],
                "events": [{"name": "island-grabbed", "poseId": "grab", "anchor": "island-grab", "point": [120, 58]}],
                "finalAnchor": {"name": "island-stand", "point": [80, 114]},
                "interruptiblePoseIds": ["approach", "stand"],
                "reducedMotion": "fixture",
            },
        ],
    }


class ValidateMotionSamplesTests(unittest.TestCase):
    def test_valid_three_action_sample_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.assertEqual([], validate_document(valid_document(root), root))

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
            document["actions"][1]["events"] = []
            errors = validate_document(document, root)
            self.assertTrue(any("landed" in error for error in errors))

    def test_forbidden_layer_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            document = valid_document(root)
            document["layers"].append("mouth")
            errors = validate_document(document, root)
            self.assertTrue(any("layers must equal" in error for error in errors))


if __name__ == "__main__":
    unittest.main()

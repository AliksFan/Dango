from __future__ import annotations

import json
import subprocess
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

from validate_export import validate


def png_chunk(chunk_type: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(chunk_type)
    crc = zlib.crc32(data, crc) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", crc)


def write_png(path: Path, width: int = 2, height: int = 2, color_type: int = 6) -> None:
    channels = 4 if color_type == 6 else 3
    raw = b"".join(b"\x00" + bytes([128] * width * channels) for _ in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
    payload = (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", header)
        + png_chunk(b"IDAT", zlib.compress(raw))
        + png_chunk(b"IEND", b"")
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def write_provenance(path: Path, files: list[dict[str, str]]) -> None:
    path.write_text(json.dumps({"version": 1, "files": files}), encoding="utf-8")


def entry(path: str) -> dict[str, str]:
    return {
        "path": path,
        "sourceType": "project-code-generated",
        "source": "unit test fixture",
        "license": "MIT",
    }


class ValidateExportTests(unittest.TestCase):
    def test_valid_nonempty_rgba_export_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "idle" / "body-000.png")
            write_png(root / "idle" / "body-001.png")
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("idle/body-000.png"), entry("idle/body-001.png")])

            result = validate(root, provenance)

            self.assertEqual(2, result["checkedFiles"])
            self.assertEqual([], result["errors"])
            self.assertEqual({"width": 2, "height": 2}, result["canvas"])

    def test_zero_png_files_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            provenance = root / "provenance.json"
            write_provenance(provenance, [])

            result = validate(root, provenance)

            self.assertIn("asset directory contains zero PNG files", result["errors"])

    def test_corrupt_png_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "bad.png").write_bytes(b"not a png")
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("bad.png")])

            result = validate(root, provenance)

            self.assertTrue(any("invalid PNG signature" in error for error in result["errors"]))

    def test_corrupt_compressed_pixels_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "bad-stream.png")
            data = bytearray((root / "bad-stream.png").read_bytes())
            idat = data.index(b"IDAT")
            data[idat + 4] ^= 0xFF
            chunk_length = struct.unpack(">I", data[idat - 4 : idat])[0]
            chunk_data = bytes(data[idat + 4 : idat + 4 + chunk_length])
            crc = zlib.crc32(b"IDAT")
            crc = zlib.crc32(chunk_data, crc) & 0xFFFFFFFF
            data[idat + 4 + chunk_length : idat + 8 + chunk_length] = struct.pack(">I", crc)
            (root / "bad-stream.png").write_bytes(data)
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("bad-stream.png")])

            result = validate(root, provenance)

            self.assertTrue(any("compressed pixel stream" in error for error in result["errors"]))

    def test_rgb_without_alpha_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "rgb.png", color_type=2)
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("rgb.png")])

            result = validate(root, provenance)

            self.assertTrue(any("no alpha" in error for error in result["errors"]))

    def test_mismatched_canvas_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "a.png", width=2)
            write_png(root / "b.png", width=3)
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("a.png"), entry("b.png")])

            result = validate(root, provenance)

            self.assertTrue(any("does not match" in error for error in result["errors"]))

    def test_missing_and_unsafe_provenance_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "body.png")
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("../body.png")])

            result = validate(root, provenance)

            self.assertTrue(any("safe package-relative path" in error for error in result["errors"]))
            self.assertTrue(any("missing provenance entry" in error for error in result["errors"]))

    def test_unsupported_source_type_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "body.png")
            bad_entry = entry("body.png")
            bad_entry["sourceType"] = "unknown"
            provenance = root / "provenance.json"
            write_provenance(provenance, [bad_entry])

            result = validate(root, provenance)

            self.assertTrue(any("unsupported sourceType" in error for error in result["errors"]))

    def test_provenance_outside_asset_directory_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            asset_dir = root / "assets"
            asset_dir.mkdir()
            write_png(asset_dir / "body.png")
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("body.png")])

            result = validate(asset_dir, provenance)

            self.assertIn("provenance file must exist inside the asset directory", result["errors"])

    def test_cli_returns_zero_for_valid_export_and_one_for_empty_export(self) -> None:
        script = Path(__file__).with_name("validate_export.py")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            write_png(root / "body.png")
            provenance = root / "provenance.json"
            write_provenance(provenance, [entry("body.png")])

            valid = subprocess.run(
                [sys.executable, str(script), str(root), "--provenance", str(provenance)],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, valid.returncode, valid.stdout + valid.stderr)

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            provenance = root / "provenance.json"
            write_provenance(provenance, [])

            empty = subprocess.run(
                [sys.executable, str(script), str(root), "--provenance", str(provenance)],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, empty.returncode, empty.stdout + empty.stderr)


if __name__ == "__main__":
    unittest.main()

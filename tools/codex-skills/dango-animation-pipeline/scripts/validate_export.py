#!/usr/bin/env python3
"""Validate Dango PNG exports and their provenance using the Python standard library."""

from __future__ import annotations

import argparse
import json
import struct
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
ALPHA_COLOR_TYPES = {4, 6}
CHANNELS_BY_COLOR_TYPE = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
BIT_DEPTHS_BY_COLOR_TYPE = {
    0: {1, 2, 4, 8, 16},
    2: {8, 16},
    3: {1, 2, 4, 8},
    4: {8, 16},
    6: {8, 16},
}
SOURCE_TYPES = {
    "project-created",
    "ai-draft-human-reviewed",
    "project-code-generated",
}


@dataclass(frozen=True)
class PngInfo:
    width: int
    height: int
    has_alpha: bool


def read_png(path: Path) -> PngInfo:
    data = path.read_bytes()
    if not data.startswith(PNG_SIGNATURE):
        raise ValueError("invalid PNG signature")

    offset = len(PNG_SIGNATURE)
    ihdr: PngInfo | None = None
    has_idat = False
    has_iend = False
    has_trns = False
    idat_parts: list[bytes] = []
    pixel_shape: tuple[int, int, int, int] | None = None

    while offset < len(data):
        if offset + 12 > len(data):
            raise ValueError("truncated PNG chunk")
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        chunk_type = data[offset + 4 : offset + 8]
        chunk_end = offset + 12 + length
        if chunk_end > len(data):
            raise ValueError("truncated PNG chunk data")
        chunk_data = data[offset + 8 : offset + 8 + length]
        expected_crc = struct.unpack(">I", data[offset + 8 + length : chunk_end])[0]
        actual_crc = zlib.crc32(chunk_type)
        actual_crc = zlib.crc32(chunk_data, actual_crc) & 0xFFFFFFFF
        if actual_crc != expected_crc:
            raise ValueError(f"CRC mismatch in {chunk_type.decode('ascii', errors='replace')}")

        if chunk_type == b"IHDR":
            if ihdr is not None or length != 13:
                raise ValueError("invalid IHDR")
            width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", chunk_data
            )
            if width <= 0 or height <= 0:
                raise ValueError("invalid image dimensions")
            if color_type not in CHANNELS_BY_COLOR_TYPE:
                raise ValueError("invalid color type")
            if bit_depth not in BIT_DEPTHS_BY_COLOR_TYPE[color_type]:
                raise ValueError("invalid bit depth for color type")
            if compression != 0 or filtering != 0 or interlace != 0:
                raise ValueError("unsupported PNG header")
            if bit_depth not in {1, 2, 4, 8, 16}:
                raise ValueError("invalid bit depth")
            ihdr = PngInfo(width, height, color_type in ALPHA_COLOR_TYPES)
            pixel_shape = (width, height, bit_depth, color_type)
        elif chunk_type == b"tRNS":
            has_trns = True
        elif chunk_type == b"IDAT":
            has_idat = True
            idat_parts.append(chunk_data)
        elif chunk_type == b"IEND":
            has_iend = True
            offset = chunk_end
            break

        offset = chunk_end

    if ihdr is None or pixel_shape is None or not has_idat or not has_iend:
        raise ValueError("PNG is missing IHDR, IDAT, or IEND")
    if offset != len(data):
        raise ValueError("unexpected data after IEND")
    width, height, bit_depth, color_type = pixel_shape
    row_bytes = (width * CHANNELS_BY_COLOR_TYPE[color_type] * bit_depth + 7) // 8
    expected_length = height * (row_bytes + 1)
    try:
        raw_pixels = zlib.decompress(b"".join(idat_parts))
    except zlib.error as exc:
        raise ValueError(f"invalid compressed pixel stream: {exc}") from exc
    if len(raw_pixels) != expected_length:
        raise ValueError("decoded pixel stream has an invalid length")
    for row_start in range(0, expected_length, row_bytes + 1):
        if raw_pixels[row_start] not in range(5):
            raise ValueError("decoded pixel stream uses an invalid filter")
    return PngInfo(ihdr.width, ihdr.height, ihdr.has_alpha or has_trns)


def safe_relative_path(value: object) -> str | None:
    if not isinstance(value, str) or not value:
        return None
    candidate = PurePosixPath(value)
    if candidate.is_absolute() or ".." in candidate.parts or "\\" in value:
        return None
    return candidate.as_posix()


def load_provenance(path: Path) -> tuple[dict[str, dict[str, object]], list[str]]:
    errors: list[str] = []
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        return {}, [f"provenance cannot be read: {exc}"]

    files = document.get("files") if isinstance(document, dict) else None
    if not isinstance(document, dict) or document.get("version") != 1:
        errors.append("provenance.version must equal 1")
    if not isinstance(files, list):
        errors.append("provenance.files must be an array")
        return {}, errors

    entries: dict[str, dict[str, object]] = {}
    for index, entry in enumerate(files):
        if not isinstance(entry, dict):
            errors.append(f"provenance.files[{index}] must be an object")
            continue
        relative = safe_relative_path(entry.get("path"))
        if relative is None:
            errors.append(f"provenance.files[{index}].path must be a safe package-relative path")
            continue
        if relative in entries:
            errors.append(f"duplicate provenance path: {relative}")
            continue
        for field in ("sourceType", "source", "license"):
            if not isinstance(entry.get(field), str) or not entry[field].strip():
                errors.append(f"provenance entry {relative} is missing {field}")
        if entry.get("sourceType") not in SOURCE_TYPES:
            errors.append(f"provenance entry {relative} has an unsupported sourceType")
        entries[relative] = entry
    return entries, errors


def validate(asset_dir: Path, provenance_path: Path) -> dict[str, object]:
    errors: list[str] = []
    try:
        root = asset_dir.resolve(strict=True)
    except OSError as exc:
        return {"checkedFiles": 0, "errors": [f"asset directory cannot be read: {exc}"]}
    if not root.is_dir():
        return {"checkedFiles": 0, "errors": ["asset path is not a directory"]}

    try:
        provenance_path.resolve(strict=True).relative_to(root)
    except (OSError, ValueError):
        return {
            "checkedFiles": 0,
            "errors": ["provenance file must exist inside the asset directory"],
        }

    entries, provenance_errors = load_provenance(provenance_path)
    errors.extend(provenance_errors)
    png_paths = sorted(path for path in root.rglob("*.png") if path.is_file())
    if not png_paths:
        errors.append("asset directory contains zero PNG files")

    expected_size: tuple[int, int] | None = None
    checked = 0
    seen: set[str] = set()
    for path in png_paths:
        relative = path.relative_to(root).as_posix()
        seen.add(relative)
        if relative not in entries:
            errors.append(f"missing provenance entry: {relative}")
        try:
            info = read_png(path)
        except (OSError, ValueError) as exc:
            errors.append(f"{relative}: {exc}")
            continue
        checked += 1
        if not info.has_alpha:
            errors.append(f"{relative}: PNG has no alpha channel or transparency chunk")
        size = (info.width, info.height)
        if expected_size is None:
            expected_size = size
        elif size != expected_size:
            errors.append(
                f"{relative}: canvas {info.width}x{info.height} does not match "
                f"{expected_size[0]}x{expected_size[1]}"
            )

    for relative in sorted(entries.keys() - seen):
        target = (root / Path(*PurePosixPath(relative).parts)).resolve()
        try:
            target.relative_to(root)
        except ValueError:
            errors.append(f"provenance path escapes asset directory: {relative}")
            continue
        if relative.lower().endswith(".png") and not target.is_file():
            errors.append(f"provenance references missing PNG: {relative}")

    result: dict[str, object] = {
        "checkedFiles": checked,
        "canvas": None if expected_size is None else {"width": expected_size[0], "height": expected_size[1]},
        "errors": errors,
    }
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("asset_dir", type=Path)
    parser.add_argument("--provenance", required=True, type=Path)
    args = parser.parse_args()

    result = validate(args.asset_dir, args.provenance)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["checkedFiles"] and not result["errors"] else 1


if __name__ == "__main__":
    sys.exit(main())

"""Inspect a Git revision's reachable objects without emitting matched values.

This is a local, heuristic triage tool, not a security or copyright clearance.
The report identifies object IDs, relative paths and line numbers only. Binary
content and asset rights need separate review. Never upload its reports to CI.
"""

from __future__ import annotations

import argparse
import collections
import json
from pathlib import Path
import re
import subprocess


PATTERNS = {
    "private-key": r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----",
    "github-token": r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b",
    "openai-token": r"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{24,}\b",
    "aws-access-key": r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",
    "credential-url": r"\b(?:https?|postgres(?:ql)?|mongodb(?:\+srv)?|redis)://[^\s/:@]+:[^\s/@]+@",
    "literal-secret-candidate": r"(?i)\b(?:password|client[_-]?secret|api[_-]?key|access[_-]?token)\s*[:=]\s*[\"'][^\"'\r\n]{8,}[\"']",
    "local-workspace-path": r"(?i)(?<![A-Za-z0-9])(?:[A-Z]:[\\/]+(?:Users|0Work|0Personal|Work|Downloads)[\\/]+|/Users/|/home/)",
    "connection-string-candidate": r"(?i)\b(?:User\s*ID|Username|Password|Pwd)\s*=\s*[^;\s\"']+",
    "contact-number": r"(?i)\bQQ\s*[:：]\s*\d{5,12}\b",
    "email-candidate": r"\b[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@(?:[A-Za-z0-9-]+\.)+[A-Za-z]{2,}\b",
}
RULES = {name: re.compile(pattern) for name, pattern in PATTERNS.items()}
EXAMPLE_DOMAINS = {"example.com", "example.org", "example.net", "example.invalid"}


def scan(data: bytes, *, path: str, oid: str, scope: str) -> tuple[list[dict], bool]:
    if b"\0" in data[:8192]:
        return [], True
    findings = []
    for number, line in enumerate(data.decode("utf-8", errors="replace").splitlines(), 1):
        for name, rule in RULES.items():
            matches = list(rule.finditer(line))
            if name == "email-candidate":
                matches = [m for m in matches if m.group().rsplit("@", 1)[1].lower() not in EXAMPLE_DOMAINS]
            if matches:
                findings.append({"rule": name, "path": path, "object": oid, "line": number, "scope": scope})
    return findings, False


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)


def audit(root: Path, ref: str, include_worktree: bool = False) -> dict:
    revision = git(root, "rev-parse", "--verify", "--end-of-options", ref + "^{commit}").decode().strip()
    records = git(root, "rev-list", "--objects", revision).decode("utf-8", errors="replace").splitlines()
    findings, binaries = [], []
    counts = collections.Counter()
    contacts = 0
    process = subprocess.Popen(["git", "-C", str(root), "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    try:
        for record in records:
            oid, _, path = record.partition(" ")
            process.stdin.write((oid + "\n").encode("ascii"))
            process.stdin.flush()
            header = process.stdout.readline().decode("ascii").split()
            if len(header) != 3:
                raise RuntimeError("Cannot read a reachable Git object")
            kind, size = header[1], int(header[2])
            data = process.stdout.read(size)
            if len(data) != size or process.stdout.read(1) != b"\n":
                raise RuntimeError("Incomplete Git object stream")
            counts[kind] += 1
            if kind not in {"blob", "commit"}:
                continue
            if kind == "commit":
                contacts += bool(re.search(rb"(?m)^author .+<[^>]+>", data))
            found, binary = scan(data, path=path or "<commit>", oid=oid, scope="history")
            findings.extend(found)
            if binary:
                binaries.append({"path": path, "object": oid, "scope": "history"})
    finally:
        process.stdin.close()
        process.wait()
        process.stdout.close()
        process.stderr.close()
    if process.returncode:
        raise RuntimeError("Git object reader failed")
    if include_worktree:
        names = git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard").split(b"\0")
        for name in dict.fromkeys(names):
            if not name:
                continue
            relative = name.decode("utf-8")
            candidate = root / relative
            if not candidate.is_file() or candidate.is_symlink():
                continue
            if not candidate.resolve().is_relative_to(root):
                raise RuntimeError("A tracked file resolves outside the repository")
            found, binary = scan(candidate.read_bytes(), path=relative, oid="", scope="worktree")
            counts["worktreeFiles"] += 1
            findings.extend(found)
            if binary:
                binaries.append({"path": relative, "object": "", "scope": "worktree"})
    return {
        "schemaVersion": 1, "revision": revision,
        "coverage": "All reachable commit and blob objects; optional nonignored working tree files. No other refs or submodules. Binary payloads, archives, LFS content and asset rights require separate review.",
        "objects": dict(counts), "commitsWithAuthorContactMetadata": contacts,
        "countsByRule": dict(collections.Counter(f["rule"] for f in findings)),
        "findings": findings, "binaryObjectsNotTextScanned": binaries,
        "clearanceGranted": False,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", default="HEAD")
    parser.add_argument("--worktree", action="store_true")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").decode().strip()).resolve()
    report = audit(root, args.ref, args.worktree)
    output = args.output.resolve()
    if not output.is_relative_to(root / "artifacts"):
        parser.error("Keep private reports under the ignored artifacts directory")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"revision": report["revision"], "objects": report["objects"], "countsByRule": report["countsByRule"], "binaryObjectsNotTextScanned": len(report["binaryObjectsNotTextScanned"]), "clearanceGranted": False}))


if __name__ == "__main__":
    main()

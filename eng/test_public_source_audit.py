"""Behavior checks for the redacted, local source-history triage tool."""

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("public_source_audit", Path(__file__).with_name("audit-public-source.py"))
audit_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit_module)


class PublicSourceAuditTests(unittest.TestCase):
    def test_reports_signature_and_line_without_secret_value(self):
        token = "gh" + "p_" + "a" * 30
        findings, binary = audit_module.scan(("first\n" + token).encode(), path="sample.txt", oid="abc", scope="history")
        self.assertFalse(binary)
        self.assertTrue(any(f["rule"] == "github-token" and f["line"] == 2 for f in findings))
        self.assertNotIn(token, json.dumps(findings))

    def test_binary_and_example_contacts_are_not_claimed_as_text_findings(self):
        self.assertEqual(audit_module.scan(b"\x89PNG\0payload", path="image.png", oid="abc", scope="history"), ([], True))
        findings, _ = audit_module.scan(b"fictitious@example.invalid", path="example.txt", oid="abc", scope="history")
        self.assertEqual(findings, [])

    def test_detects_json_escaped_local_paths_without_exposing_them(self):
        sample = "C:" + "\\\\" + "Users" + "\\\\" + "fictional" + "\\\\" + "workspace"
        findings, _ = audit_module.scan(sample.encode(), path="sample.json", oid="abc", scope="history")
        self.assertTrue(any(f["rule"] == "local-workspace-path" for f in findings))
        self.assertNotIn(sample, json.dumps(findings))

    def test_finds_removed_secret_in_reachable_history(self):
        # Only this test-created directory is removed, after checking its root.
        parent = Path(__file__).resolve().parents[1] / "artifacts" / "audit-tests"
        parent.mkdir(parents=True, exist_ok=True)
        temporary = tempfile.TemporaryDirectory(prefix="fixture-", dir=parent)
        root = Path(temporary.name).resolve()
        self.assertTrue(root.is_relative_to(parent.resolve()))
        token = "gh" + "p_" + "b" * 30
        def run(*args):
            return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)
        try:
            run("init", "--quiet")
            run("config", "user.name", "Fictitious Audit Fixture")
            run("config", "user.email", "audit@example.invalid")
            sample = root / "sample.txt"
            sample.write_text(token, encoding="utf-8")
            run("add", "sample.txt")
            run("commit", "--quiet", "-m", "add fictitious fixture")
            sample.write_text("clean head\n", encoding="utf-8")
            run("add", "sample.txt")
            run("commit", "--quiet", "-m", "remove fixture")
            report = audit_module.audit(root, "HEAD", True)
            self.assertEqual(report["objects"]["commit"], 2)
            findings = [f for f in report["findings"] if f["rule"] == "github-token"]
            self.assertEqual(len(findings), 1)
            self.assertEqual(findings[0]["scope"], "history")
            self.assertEqual(findings[0]["path"], "sample.txt")
            self.assertFalse(report["clearanceGranted"])
            self.assertNotIn(token, json.dumps(report))
        finally:
            self.assertTrue(root.is_relative_to(parent.resolve()))
            temporary.cleanup()


if __name__ == "__main__":
    unittest.main()

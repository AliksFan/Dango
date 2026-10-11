"""Failure-capable pure evidence/parser tests; no native load, Docker or child suite."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("verify_linux", Path(__file__).with_name("verify_linux.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class EvidenceGateTests(unittest.TestCase):
    def setUp(self):
        self.identity = dict(pid=10101, exit_code=0, expected_exit=0, passed=True, mode="suite", mutation="",
                             source_hash="A" * 64, module_hash="B" * 64, record_hash="C" * 64, required=gate.SUITE_CHECKS)
        self.report = {"Version": 1, "ProcessId": 10101, "Mode": "suite", "Mutation": "", "Pass": True,
                       "SourceHash": "A" * 64, "DllHash": "B" * 64, "BuildRecordHash": "C" * 64,
                       "ParameterHash": gate.PARAMETER_HASH, "NativePlatform": "linux-x64",
                       "Checks": [{"Name": name, "Pass": True, "Detail": "fictional pure fixture"}
                                  for name in gate.COMMON_CHECKS + gate.SUITE_CHECKS],
                       "NativeCounts": {"Created": 1, "Disposed": 1, "Reset": 0, "Add": 2, "Remove": 0, "Steps": 600},
                       "Allocation": {"Before": 0, "After": 0}, "ExternalNativeProbe": None}

    def validate(self, report=None, **overrides):
        identity = self.identity | overrides
        return gate.validate_report(report or self.report, **identity)

    def test_frozen_checklist_contains_43_unique_required_names(self):
        self.assertEqual(43, len(set(gate.COMMON_CHECKS + gate.SUITE_CHECKS)))
        self.validate()

    def test_missing_any_frozen_normal_check_fails(self):
        for name in gate.COMMON_CHECKS + gate.SUITE_CHECKS:
            with self.subTest(name=name):
                report = copy.deepcopy(self.report)
                report["Checks"] = [item for item in report["Checks"] if item["Name"] != name]
                with self.assertRaises(gate.GateError):
                    self.validate(report)

    def test_boolean_pid_and_allocation_cannot_impersonate_integer(self):
        for field in ("ProcessId", "Version"):
            report = copy.deepcopy(self.report)
            report[field] = True
            with self.assertRaises(gate.GateError):
                self.validate(report)
        for group, field in (("NativeCounts", "Created"), ("Allocation", "Before")):
            report = copy.deepcopy(self.report)
            report[group][field] = False
            with self.assertRaises(gate.GateError):
                self.validate(report)

    def test_stale_identities_and_wrong_actual_exit_fail(self):
        for key, value in (("ProcessId", 90909), ("SourceHash", "D" * 64), ("DllHash", "D" * 64),
                           ("BuildRecordHash", "D" * 64), ("ParameterHash", "D" * 64),
                           ("NativePlatform", "win-x64"), ("Mode", "consume"), ("Mutation", "oldMove")):
            report = copy.deepcopy(self.report)
            report[key] = value
            with self.subTest(field=key), self.assertRaises(gate.GateError):
                self.validate(report)
        with self.assertRaises(gate.GateError):
            self.validate(exit_code=1)

    def test_duplicate_check_and_exception_red_are_rejected(self):
        report = copy.deepcopy(self.report)
        report["Checks"].append(copy.deepcopy(report["Checks"][0]))
        with self.assertRaises(gate.GateError):
            self.validate(report)
        report = copy.deepcopy(self.report)
        report["Checks"].append({"Name": "exception", "Pass": False, "Detail": "fictional failure"})
        report["Pass"], report["Mutation"] = False, "oldMove"
        with self.assertRaises(gate.GateError):
            self.validate(report, exit_code=1, expected_exit=1, passed=False, mutation="oldMove", required=())

    def test_wrong_red_oracle_does_not_satisfy_mutation(self):
        report = copy.deepcopy(self.report)
        report["Pass"], report["Mutation"] = False, "oldMove"
        report["Checks"].append({"Name": "other-red", "Pass": False, "Detail": "fictional failure"})
        with self.assertRaises(gate.GateError):
            self.validate(report, exit_code=1, expected_exit=1, passed=False, mutation="oldMove", required=())
        report["Checks"][-1]["Name"] = "stale-intent-not-inherited"
        self.validate(report, exit_code=1, expected_exit=1, passed=False, mutation="oldMove", required=())

    def test_unreturned_native_allocation_and_owner_fail(self):
        for group, field, value in (("NativeCounts", "Disposed", 0), ("Allocation", "After", 1), ("Allocation", "Before", 1)):
            report = copy.deepcopy(self.report)
            report[group][field] = value
            with self.assertRaises(gate.GateError):
                self.validate(report)

    def test_native_fault_mutant_requires_real_separate_probe_identity(self):
        report = copy.deepcopy(self.report)
        report["Pass"], report["Mutation"] = False, "faultAutoRebirth"
        next(check for check in report["Checks"] if check["Name"] == "native-node-fault-probe")["Pass"] = False
        overrides = dict(exit_code=1, expected_exit=1, passed=False, mutation="faultAutoRebirth", required=())
        with self.assertRaises(gate.GateError):
            self.validate(report, **overrides)
        report["ExternalNativeProbe"] = {"ProcessId": 10102, "ExitCode": 1, "LastSafeTick": 1, "Steps": 1,
                                          "NativeErrorCode": -422, "Created": 1, "Disposed": 1, "Before": 0, "After": 0}
        self.validate(report, **overrides)
        report["ExternalNativeProbe"]["ProcessId"] = 10101
        with self.assertRaises(gate.GateError):
            self.validate(report, **overrides)

    def test_duplicate_json_keys_are_not_silently_last_value_wins(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fictional.json"
            path.write_text('{"Version":1,"Version":2}', encoding="utf-8")
            with self.assertRaises(gate.GateError):
                gate.load_json(path)
            path.write_text('{"value":NaN}', encoding="utf-8")
            with self.assertRaises(gate.GateError):
                gate.load_json(path)

    def test_source_hash_uses_paths_and_bytes_and_excludes_only_bin_obj(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "source").mkdir()
            (root / "source/a.cs").write_bytes(b"alpha")
            (root / "source/bin").mkdir()
            (root / "source/bin/ignored").write_bytes(b"not source")
            original, entries = gate.inventory(root, ("source",))
            self.assertEqual(original, gate.sha(b"source/a.csalpha"))
            self.assertEqual(1, len(entries))
            (root / "source/bin/ignored").write_bytes(b"changed build output")
            self.assertEqual(original, gate.inventory(root, ("source",))[0])
            (root / "source/a.cs").rename(root / "source/b.cs")
            self.assertNotEqual(original, gate.inventory(root, ("source",))[0])

    def test_record_contract_rejects_unknown_fields_wrong_origin_and_boolean_version(self):
        record = {"Version": 1, "Platform": "linux-x64", "SourceHash": "A" * 64, "ModuleHash": "B" * 64,
                  "NativeProbeHash": "C" * 64, "Box2DCommit": gate.COMMIT, "Box2DArchiveHash": gate.ARCHIVE_HASH,
                  "Box2DLicenseHash": gate.LICENSE_HASH, "BuildConfiguration": "Release", "AbiVersion": 1,
                  "Compiler": "fictional gcc", "Exports": ["fictional_export"]}
        def validate(value):
            gate.validate_record(value, "A" * 64, "B" * 64, "C" * 64, ["fictional_export"])
        validate(record)
        for key, value in (("Version", True), ("AbiVersion", True), ("Platform", "win-x64"),
                           ("SourceHash", "D" * 64), ("Extra", "unapproved"), ("Compiler", "")):
            with self.subTest(key=key), self.assertRaises(gate.GateError):
                validate(record | {key: value})

    def test_actual_box2d_single_source_tamper_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "box2d"
            evidence = Path(directory) / "evidence"
            (root / "src").mkdir(parents=True)
            evidence.mkdir()
            license_path = Path(__file__).resolve().parents[3] / "native/box2d/LICENSE.box2d.txt"
            (root / "LICENSE").write_bytes(license_path.read_bytes())
            source = root / "src/fictional.c"
            source.write_bytes(b"fictional original source\n")
            tree_hash, files = gate.inventory(root)
            gate.write_json(evidence / "box2d-source-files.json", {"Commit": gate.COMMIT, "ArchiveHash": gate.ARCHIVE_HASH,
                            "LicenseHash": gate.LICENSE_HASH, "TreeHash": tree_hash, "Files": files})
            unchanged = gate.verify_box2d_tree(root, evidence)
            self.assertTrue(unchanged["Pass"])
            self.assertEqual(tree_hash, unchanged["TreeHash"])
            source.write_bytes(b"fictional tampered source\n")
            with self.assertRaisesRegex(gate.GateError, "build source tree changed"):
                gate.verify_box2d_tree(root, evidence)

    def test_box2d_full_inventory_and_provenance_are_required(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "box2d"
            evidence = Path(directory) / "evidence"
            root.mkdir()
            evidence.mkdir()
            license_path = Path(__file__).resolve().parents[3] / "native/box2d/LICENSE.box2d.txt"
            (root / "LICENSE").write_bytes(license_path.read_bytes())
            tree_hash, files = gate.inventory(root)
            frozen = {"Commit": gate.COMMIT, "ArchiveHash": gate.ARCHIVE_HASH, "LicenseHash": gate.LICENSE_HASH,
                      "TreeHash": tree_hash, "Files": files}
            for field, value in (("Commit", "0" * 40), ("ArchiveHash", "0" * 64), ("LicenseHash", "0" * 64),
                                 ("TreeHash", "0" * 64), ("Files", [])):
                gate.write_json(evidence / "box2d-source-files.json", frozen | {field: value})
                with self.subTest(field=field), self.assertRaises(gate.GateError):
                    gate.verify_box2d_tree(root, evidence)


if __name__ == "__main__":
    unittest.main()

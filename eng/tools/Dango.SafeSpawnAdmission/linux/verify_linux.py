#!/usr/bin/env python3
"""Independent P1 Linux evidence gate; standard library only, no network access."""

import argparse
import copy
import ctypes
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import stat
import struct
import subprocess
import sys
import time
import uuid
import zipfile

SOURCE_DIRS = ("eng/tools/Dango.SafeSpawnAdmission", "eng/tools/Dango.SafeSpawnAdmission.Core",
               "src/Dango.Physics", "eng/native/box2d")
COMMIT = "8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3"
ARCHIVE_HASH = "FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721"
LICENSE_HASH = "68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2"
PARAMETER_HASH = "7257CF6852ED6EF932485F02610E3C87D8B9BE99D21EBF3626A22D0305245506"
EMPTY_GUID = "00000000-0000-0000-0000-000000000000"
MUTATIONS = ("healthyReset", "oldMove", "oldJump", "halfReady", "skipSpawnOverlap", "skipSpawnBounds",
             "skipSpawnGround", "oldEpoch", "noReason", "faultAutoRebirth", "observerIgnored",
             "earlyRelease", "badRecordAsNew", "onlyPetId", "oldWriter", "unknownFaultAsLost", "staleReady")
COMMON_CHECKS = ("frozen-dll", "native-allocation-returned", "native-owner-disposed")
CONTINUITY_CHECKS = tuple(f"{scene}-{transition}-120-bits" for scene in ("push", "stack", "airborne")
                          for transition in ("reconnect", "takeover", "pause"))
SUITE_CHECKS = (
    "planner-left-tangent-rejected", "planner-right-tangent-rejected", "planner-invalid-bounds-rejected",
    "planner-group-tangent-rejected", "planner-live-capacity-rejected", "tick0-1", "gravity-1",
    "600-native-steps-1", "checkpoint-unsupported-1", "tick0-2", "gravity-2", "600-native-steps-2",
    "checkpoint-unsupported-2", "candidate-secondAdd-whole-dispose", "candidate-cancel-whole-dispose",
    "candidate-error-whole-dispose", "cross-thread-rejected", "native-candidate-latest-facts-race",
    "lower-actually-supports", "remove-lower-survivor-retained", "new-member-space-precheck",
    "live-member-real-reentry", "native-node-fault-probe", "real-native-raw-right-containment",
    "real-native-raw-left-containment", "real-native-raw-ground-containment", "readonly-dto-raw-ground-containment",
    "readonly-dto-raw-right-containment", "tick0-velocity-stops-before-publication",
    "tick0-support-stops-before-publication", "tick0-cooldown-stops-before-publication") + CONTINUITY_CHECKS
ORACLES = {
    "halfReady": ("whole-group-before-ready",), "skipSpawnOverlap": ("independent-envelope-before-add",),
    "skipSpawnBounds": ("independent-envelope-before-add",), "skipSpawnGround": ("independent-envelope-before-add",),
    "oldMove": ("stale-intent-not-inherited",), "oldJump": ("stale-intent-not-inherited",),
    "oldEpoch": ("epoch-and-reason-native-reuse",), "noReason": ("epoch-and-reason-native-reuse",),
    "faultAutoRebirth": ("native-node-fault-probe",), "healthyReset": CONTINUITY_CHECKS,
}
RECORD_FIELDS = {"Version", "Platform", "SourceHash", "ModuleHash", "NativeProbeHash", "Box2DCommit",
                 "Box2DArchiveHash", "Box2DLicenseHash", "BuildConfiguration", "AbiVersion", "Compiler", "Exports"}


class GateError(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise GateError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def file_hash(path):
    return sha(Path(path).read_bytes())


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2, ensure_ascii=True, allow_nan=False) + "\n", encoding="utf-8")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def load_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object,
                      parse_constant=lambda value: (_ for _ in ()).throw(GateError(f"Nonfinite JSON: {value}")))


def integer(value, minimum=0):
    return type(value) is int and value >= minimum


def finite_number(value, minimum=0):
    return type(value) in (int, float) and math.isfinite(value) and value >= minimum


def guid(value, allow_empty=False):
    if type(value) is not str:
        return False
    try:
        parsed = uuid.UUID(value)
        return str(parsed) == value and (allow_empty or parsed.int != 0)
    except ValueError:
        return False


def inventory(root, directories=None):
    root = Path(root)
    files = []
    for base in (root / name for name in directories) if directories else (root,):
        require(base.is_dir(), f"Missing source directory: {base}")
        for path in base.rglob("*"):
            require(not path.is_symlink(), f"Input symlinks are not admitted: {path}")
            if path.is_file() and (not directories or not {"bin", "obj"}.intersection(path.relative_to(root).parts)):
                files.append(path)
    # .NET StringComparer.Ordinal compares UTF-16 code units.
    files.sort(key=lambda p: p.relative_to(root).as_posix().encode("utf-16-be"))
    result, hasher = [], hashlib.sha256()
    for path in files:
        name = path.relative_to(root).as_posix()
        data = path.read_bytes()
        hasher.update(name.encode("utf-8"))
        hasher.update(data)
        result.append({"Path": name, "Bytes": len(data), "Sha256": sha(data)})
    return hasher.hexdigest().upper(), result


def expected_exports(root):
    header = (Path(root) / "eng/native/box2d/dango_physics_module.h").read_text(encoding="utf-8-sig")
    names = re.findall(r"DANGO_API\s+(?:int|double)\s+(dango_physics_\w+)\s*\(", header)
    require(len(names) == 35 and len(set(names)) == 35, "The frozen header must declare 35 exports")
    return sorted(names)


def elf_info(path, shared):
    data = Path(path).read_bytes()
    require(len(data) >= 64 and data[:7] == b"\x7fELF\x02\x01\x01", f"Not ELF64 little-endian: {path}")
    kind, machine = struct.unpack_from("<HH", data, 16)
    require(machine == 62, f"Not ELF x86-64: {path}")
    require(kind == 3 if shared else kind in (2, 3), f"Wrong ELF object type: {path}")
    ph_offset = struct.unpack_from("<Q", data, 32)[0]
    ph_size, ph_count = struct.unpack_from("<HH", data, 54)
    require(ph_size >= 56 and ph_count > 0 and ph_offset + ph_size * ph_count <= len(data), "Invalid ELF program headers")
    has_interpreter = any(struct.unpack_from("<I", data, ph_offset + index * ph_size)[0] == 3 for index in range(ph_count))
    require(not has_interpreter if shared else kind == 2 or has_interpreter,
            "ELF shared module/executable identity differs")
    if not shared:
        require(os.access(path, os.X_OK), f"Native probe is not executable: {path}")
    return {"Class": "ELF64", "Endian": "little", "Machine": machine, "Type": kind,
            "Bytes": len(data), "Sha256": sha(data)}


def prepare(args):
    evidence, workspace, source = Path(args.evidence), Path(args.workspace), Path(args.source_root)
    evidence.mkdir(parents=True, exist_ok=True)
    require(not (workspace / "repo").exists(), "Owned workspace must be new")
    dependency = load_json(source / "eng/native/box2d/dependency.json")
    require(dependency["commit"] == COMMIT and dependency["sourceArchive"]["sha256"] == ARCHIVE_HASH and
            dependency["license"]["sha256"] == LICENSE_HASH, "Pinned Box2D dependency changed")
    require(file_hash(args.archive) == ARCHIVE_HASH, "Official Box2D archive SHA256 differs")
    require(file_hash(source / "eng/native/box2d" / dependency["license"]["path"]) == LICENSE_HASH,
            "Repository Box2D license SHA256 differs")
    source_hash, source_files = inventory(source, SOURCE_DIRS)
    repo = workspace / "repo"
    for directory in SOURCE_DIRS:
        shutil.copytree(source / directory, repo / directory, ignore=shutil.ignore_patterns("bin", "obj"))
    root_files = ("global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config")
    root_inventory = []
    for name in root_files:
        path = source / name
        if path.is_file():
            require(not path.is_symlink(), "Root build files must not be symlinks")
            shutil.copyfile(path, repo / name)
            root_inventory.append({"Path": name, "Bytes": path.stat().st_size, "Sha256": file_hash(path)})
    require((repo / "global.json").is_file() and (repo / "Directory.Build.props").is_file(), "Root build files missing")
    box_root = workspace / "box2d"
    box_root.mkdir()
    prefix = f"box2d-{COMMIT}/"
    with zipfile.ZipFile(args.archive) as archive:
        for entry in archive.infolist():
            parts = Path(entry.filename).parts
            require(entry.filename.startswith(prefix) and not Path(entry.filename).is_absolute() and
                    ".." not in parts and "\\" not in entry.filename and
                    not stat.S_ISLNK(entry.external_attr >> 16), "Unsafe/unexpected archive member")
        license_bytes = archive.read(prefix + "LICENSE")
        require(sha(license_bytes) == LICENSE_HASH, "Official archive license SHA256 differs")
        archive.extractall(box_root)
    copied_hash, copied_files = inventory(repo, SOURCE_DIRS)
    require(copied_hash == source_hash and copied_files == source_files, "Copied source bytes differ")
    box_hash, box_files = inventory(box_root / f"box2d-{COMMIT}")
    write_json(evidence / "linux-source-files.json", {"SourceHash": source_hash, "Files": source_files, "RootBuildFiles": root_inventory})
    write_json(evidence / "box2d-source-files.json", {"Commit": COMMIT, "ArchiveHash": ARCHIVE_HASH,
               "LicenseHash": LICENSE_HASH, "TreeHash": box_hash, "Files": box_files})


def verify_box2d_tree(root, evidence):
    frozen = load_json(Path(evidence) / "box2d-source-files.json")
    require(type(frozen) is dict and set(frozen) == {"Commit", "ArchiveHash", "LicenseHash", "TreeHash", "Files"},
            "Frozen Box2D inventory fields differ")
    require(frozen["Commit"] == COMMIT and frozen["ArchiveHash"] == ARCHIVE_HASH and frozen["LicenseHash"] == LICENSE_HASH,
            "Frozen Box2D provenance differs")
    require(file_hash(Path(root) / "LICENSE") == LICENSE_HASH, "Actual Box2D license changed after extraction")
    actual_hash, actual_files = inventory(root)
    require(actual_hash == frozen["TreeHash"] and actual_files == frozen["Files"],
            "Actual Box2D build source tree changed after extraction")
    return {"Pass": True, "Commit": COMMIT, "ArchiveHash": ARCHIVE_HASH, "LicenseHash": LICENSE_HASH,
            "TreeHash": actual_hash, "FileCount": len(actual_files)}


def check_box2d(args):
    checked = verify_box2d_tree(args.box2d_root, args.evidence)
    checked["Stage"] = args.stage
    write_json(Path(args.evidence) / ("box2d-check-" + args.stage + ".json"), checked)
    print(f"Box2D {args.stage}: complete actual source inventory unchanged", flush=True)


def record(args):
    require(sys.platform == "linux" and platform.machine() == "x86_64", "Record requires actual Linux x86-64")
    evidence = Path(args.evidence)
    source_hash, _ = inventory(args.source_root, SOURCE_DIRS)
    require(source_hash == load_json(evidence / "linux-source-files.json")["SourceHash"], "Source changed during build")
    module_info, probe_info = elf_info(args.module, True), elf_info(args.probe, False)
    exports = expected_exports(args.source_root)
    nm = subprocess.run(["nm", "-D", "--defined-only", args.module], check=True, capture_output=True, text=True)
    (evidence / "module-exports.log").write_text(nm.stdout, encoding="utf-8")
    actual_exports = {line.split()[-1] for line in nm.stdout.splitlines() if line.split()}
    require(set(exports).issubset(actual_exports), "Required module exports are missing")
    library = ctypes.CDLL(str(Path(args.module).resolve()))
    for name in exports:
        getattr(library, name)
    abi = library.dango_physics_get_abi_version
    abi.argtypes, abi.restype = [], ctypes.c_int
    require(abi() == 1, "Actual native loader returned wrong ABI")
    allocated = library.dango_physics_get_allocated_bytes
    allocated.argtypes, allocated.restype = [], ctypes.c_int
    require(allocated() == 0, "Record creation must not create native worlds")
    compiler = subprocess.run(["gcc", "--version"], check=True, capture_output=True, text=True).stdout.splitlines()[0]
    build_record = {"Version": 1, "Platform": "linux-x64", "SourceHash": source_hash,
                    "ModuleHash": module_info["Sha256"], "NativeProbeHash": probe_info["Sha256"],
                    "Box2DCommit": COMMIT, "Box2DArchiveHash": ARCHIVE_HASH, "Box2DLicenseHash": LICENSE_HASH,
                    "BuildConfiguration": "Release", "AbiVersion": 1, "Compiler": compiler, "Exports": exports}
    write_json(evidence / "linux-build-record.json", build_record)
    write_json(evidence / "linux-native-files.json", {"Module": module_info, "Probe": probe_info,
               "ActualAbi": 1, "RequiredExports": exports, "AllExports": sorted(actual_exports), "AllocatedBytes": allocated()})


def validate_record(record_value, source_hash, module_hash, probe_hash, exports):
    require(type(record_value) is dict and set(record_value) == RECORD_FIELDS, "Build record fields differ")
    require(type(record_value["Version"]) is int and record_value["Version"] == 1 and
            type(record_value["AbiVersion"]) is int and record_value["AbiVersion"] == 1, "Build record version/ABI differs")
    expected = {"Platform": "linux-x64", "SourceHash": source_hash, "ModuleHash": module_hash,
                "NativeProbeHash": probe_hash, "Box2DCommit": COMMIT, "Box2DArchiveHash": ARCHIVE_HASH,
                "Box2DLicenseHash": LICENSE_HASH, "BuildConfiguration": "Release", "Exports": exports}
    require(all(record_value[key] == value for key, value in expected.items()), "Build record provenance differs")
    require(type(record_value["Compiler"]) is str and bool(record_value["Compiler"].strip()), "Missing actual compiler")


def validate_report(report, *, pid, exit_code, expected_exit, passed, mode, mutation, source_hash,
                    module_hash, record_hash, required=(), profile_rejection=False):
    require(type(report) is dict, "Evidence must be an object")
    require(type(report.get("Version")) is int and report["Version"] == 1 and
            integer(report.get("ProcessId"), 1) and report["ProcessId"] == pid and exit_code == expected_exit,
            "Actual child PID/version/exit differs")
    require(report.get("Mode") == mode and report.get("Mutation") == mutation, "Requested mode/mutation differs")
    require(report.get("SourceHash") == source_hash and report.get("DllHash") == module_hash and
            report.get("ParameterHash") == PARAMETER_HASH and report.get("NativePlatform") == "linux-x64" and
            report.get("BuildRecordHash") == record_hash, "Executing source/binary/parameter/profile identity differs")
    require(type(report.get("Pass")) is bool and report["Pass"] == passed, "Pass must be the expected boolean")
    checks = report.get("Checks")
    require(type(checks) is list and checks, "Missing checks")
    names = []
    for check in checks:
        require(type(check) is dict and type(check.get("Name")) is str and bool(check["Name"]) and
                type(check.get("Pass")) is bool and type(check.get("Detail")) is str, "Invalid check field types")
        require(check["Name"] != "exception", "An exception cannot satisfy an intentional red oracle")
        names.append(check["Name"])
    require(len(set(names)) == len(names), "Duplicate check names")
    common = ("native-allocation-returned", "native-owner-disposed") if profile_rejection else COMMON_CHECKS
    require(set(common + tuple(required)).issubset(names), "Required independent check names missing")
    require(all(check["Pass"] for check in checks if check["Name"] in common), "Native ownership/profile common gate failed")
    if passed:
        require(all(check["Pass"] for check in checks), "A normal gate contains a red check")
    else:
        oracle = ("native-binary-profile",) if profile_rejection else ORACLES.get(mutation, (mutation,))
        require(any(check["Name"] in oracle and check["Pass"] is False for check in checks), "The specified mutation oracle is not red")
    counts, allocation = report.get("NativeCounts"), report.get("Allocation")
    require(type(counts) is dict and all(integer(counts.get(key)) for key in ("Created", "Disposed", "Reset", "Add", "Remove", "Steps")),
            "Native counts must contain nonnegative integers")
    require(type(allocation) is dict and integer(allocation.get("Before")) and integer(allocation.get("After")), "Allocation types differ")
    require(counts["Created"] == counts["Disposed"] and allocation["Before"] == allocation["After"] == 0,
            "Actual native ownership/allocation did not return to zero")
    external = report.get("ExternalNativeProbe")
    if external is not None:
        require(type(external) is dict and integer(external.get("ProcessId"), 1) and external["ProcessId"] != pid and
                all(integer(external.get(key)) for key in ("LastSafeTick", "Steps", "Created", "Disposed", "Before", "After")) and
                type(external.get("NativeErrorCode")) is int and type(external.get("ExitCode")) is int,
                "Native probe PID/count/error types differ")
        require(external["ExitCode"] == (1 if mutation == "faultAutoRebirth" else 0) and external["Created"] > 0 and
                external["Created"] == external["Disposed"] and external["Before"] == external["After"] == 0,
                "Independent native probe did not meet actual exit/ownership gate")
    require(mutation != "faultAutoRebirth" or external is not None, "Native fault mutant requires independent probe")
    return report


class Runner:
    def __init__(self, args):
        self.args = args
        self.evidence = Path(args.evidence)
        require(not self.evidence.exists() or not any(self.evidence.iterdir()), "Run evidence must be new or empty")
        self.evidence.mkdir(parents=True, exist_ok=True)
        self.source_hash, self.source_files = inventory(args.source_root, SOURCE_DIRS)
        self.module_hash, self.probe_hash, self.tool_hash = (file_hash(path) for path in (args.module, args.probe, args.tool))
        self.record_bytes = Path(args.build_record).read_bytes()
        self.record_hash = sha(self.record_bytes)
        self.build_record = load_json(args.build_record)
        validate_record(self.build_record, self.source_hash, self.module_hash, self.probe_hash, expected_exports(args.source_root))
        elf_info(args.module, True)
        elf_info(args.probe, False)
        self.root_files = []
        for name in ("global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "nuget.config"):
            path = Path(args.source_root) / name
            if path.is_file():
                self.root_files.append({"Path": name, "Bytes": path.stat().st_size, "Sha256": file_hash(path)})
        self.runtime_files = [{"Path": str(path), "Bytes": path.stat().st_size, "Sha256": file_hash(path)}
                              for path in sorted(Path(args.tool).parent.iterdir()) if path.is_file()]
        self.processes, self.recoveries, self.healthy_digests = [], [], []

    def invoke(self, name, arguments, *, mode="suite", mutation="", passed=True, required=(), record=None, profile_rejection=False):
        import resource  # Linux only; helpers remain testable on Windows.
        output = self.evidence / (name + ".json")
        record = Path(record or self.args.build_record)
        record_hash = file_hash(record)
        command = ["dotnet", self.args.tool, "--dll", self.args.module, "--probe", self.args.probe,
                   "--linux-build-record", str(record), "--output", str(output)] + list(arguments)
        started = time.monotonic()
        before = resource.getrusage(resource.RUSAGE_CHILDREN)
        child = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
        timed_out = False
        try:
            stdout, stderr = child.communicate(timeout=60)
        except subprocess.TimeoutExpired:
            timed_out = True
            os.killpg(child.pid, signal.SIGKILL)
            stdout, stderr = child.communicate()
        after = resource.getrusage(resource.RUSAGE_CHILDREN)
        (self.evidence / (name + ".stdout.log")).write_bytes(stdout)
        (self.evidence / (name + ".stderr.log")).write_bytes(stderr)
        process = {"Name": name, "ProcessId": child.pid, "Exit": child.returncode, "ExpectedExit": 0 if passed else 1,
                   "ExpectedPass": passed, "Mode": mode, "Mutation": mutation, "BuildRecordHash": record_hash,
                   "TimedOut": timed_out, "Exited": child.poll() is not None, "Verified": False,
                   "CpuMilliseconds": ((after.ru_utime + after.ru_stime) - (before.ru_utime + before.ru_stime)) * 1000,
                   "WallMilliseconds": (time.monotonic() - started) * 1000,
                   "CumulativeChildrenMaxRssKiB": after.ru_maxrss, "Command": command}
        self.processes.append(process)
        write_json(self.evidence / "processes.json", self.processes)
        require(not timed_out, f"{name}: child exceeded 60 seconds; owned process group killed")
        try:
            os.killpg(child.pid, 0)
        except ProcessLookupError:
            pass
        else:
            os.killpg(child.pid, signal.SIGKILL)
            raise GateError(f"{name}: an owned descendant remained after parent exit")
        report = validate_report(load_json(output), pid=child.pid, exit_code=child.returncode,
            expected_exit=0 if passed else 1, passed=passed, mode=mode, mutation=mutation,
            source_hash=self.source_hash, module_hash=self.module_hash, record_hash=record_hash,
            required=required, profile_rejection=profile_rejection)
        process["NativeCounts"] = report["NativeCounts"]
        process["Allocation"] = report["Allocation"]
        process["ExternalNativeProbe"] = report.get("ExternalNativeProbe")
        process["Verified"] = True
        write_json(self.evidence / "processes.json", self.processes)
        print(f"{name}: actual exit {child.returncode}; verified", flush=True)
        return report

    def profiles(self):
        good = self.invoke("profilecheck", ["--mode", "profilecheck"], mode="profilecheck", required=("native-binary-profile",))
        require(good["NativeCounts"]["Created"] == good["NativeCounts"]["Reset"] == 0, "Profile check created a world")
        bad_values = {"Version": 2, "Platform": "win-x64", "SourceHash": "0" * 64, "ModuleHash": "0" * 64,
                      "NativeProbeHash": "0" * 64, "Box2DCommit": "0" * 40, "Box2DArchiveHash": "0" * 64,
                      "Box2DLicenseHash": "0" * 64, "BuildConfiguration": "Debug", "AbiVersion": 2,
                      "Exports": self.build_record["Exports"][:-1], "Compiler": ""}
        for field, value in bad_values.items():
            bad = copy.deepcopy(self.build_record)
            bad[field] = value
            path = self.evidence / ("bad-record-" + field + ".json")
            write_json(path, bad)
            rejected = self.invoke("reject-" + field, ["--mode", "profilecheck"], mode="profilecheck", passed=False,
                record=path, required=("native-binary-profile", "frozen-dll"), profile_rejection=True)
            require(rejected["NativeCounts"]["Created"] == rejected["NativeCounts"]["Reset"] == 0,
                    f"Bad {field} profile created a world")

    def cross_process(self):
        for disposition in ("lost", "released", "fault", "unknown"):
            for prefix in ((120, 1200) if disposition in ("lost", "released") else (120,)):
                name = f"{disposition}-{prefix}"
                metadata_path = self.evidence / (name + ".metadata.json")
                required = ("metadata-only-bounded",) + (("native-node-fault-probe", "producer-same-native-fault-source")
                    if disposition == "fault" else ("producer-actual-native-prefix",))
                if disposition == "released":
                    required += ("actual-qualified-release",)
                producer = self.invoke(name + "-produce", ["--mode", "produce", "--steps", str(prefix),
                    "--metadata", str(metadata_path), "--disposition", disposition], mode="produce", required=required)
                size = metadata_path.stat().st_size
                require(0 < size <= 65536, "Metadata exceeds frozen byte budget")
                metadata = load_json(metadata_path)
                receipt = load_json(str(metadata_path) + ".disposition.json")
                recovery = producer["Recovery"]
                require(type(receipt.get("Version")) is int and receipt["Version"] == 1 and receipt.get("Disposition") == disposition and
                        integer(receipt.get("ProducerProcessId"), 1) and receipt["ProducerProcessId"] == producer["ProcessId"] and
                        receipt.get("MetadataHash") == file_hash(metadata_path) and guid(metadata.get("playEpoch")) and
                        receipt.get("SourceEpoch") == metadata["playEpoch"] == recovery.get("PriorEpoch") and
                        integer(recovery.get("Tick")) and recovery["Tick"] == prefix and
                        integer(metadata.get("lastSuccessfulTick")) and metadata["lastSuccessfulTick"] == prefix and
                        metadata.get("configuration", {}).get("parameterHash") == PARAMETER_HASH,
                        "Metadata/receipt does not identify the actual producer epoch/configuration")
                if disposition == "fault":
                    external = producer.get("ExternalNativeProbe")
                    require(external is not None and external["LastSafeTick"] == prefix and external["Steps"] == prefix and
                            external["NativeErrorCode"] == -422 and external["Created"] == external["Disposed"] == 1 and
                            producer["NativeCounts"]["Created"] == 0, "Fault metadata lacks same actual native fault source")
                else:
                    require(producer["NativeCounts"]["Created"] == 1 and producer["NativeCounts"]["Reset"] == 0 and
                            producer["NativeCounts"]["Steps"] == prefix, "Prefix must use one unchanged actual native world")
                consumer_checks = (("trusted-disposition-blocks-rebirth",) if disposition in ("fault", "unknown") else
                    ("recover-tick0", "group-publication", "old-epoch-ready-input-rejected", "five-second-budget", "recovery-600-steps"))
                consumer = self.invoke(name + "-consume", ["--mode", "consume", "--metadata", str(metadata_path),
                    "--disposition", disposition], mode="consume", required=consumer_checks)
                require(producer["ProcessId"] != consumer["ProcessId"], "Producer and consumer must be different actual PIDs")
                recovered = consumer["Recovery"]
                require(recovered.get("PriorEpoch") == metadata["playEpoch"], "Consumer recovered a different source epoch")
                require(all(type(recovered.get(key)) is bool for key in ("Blocked", "Ready", "Published", "AwaitingReady")) and
                        integer(recovered.get("Tick")) and finite_number(recovered.get("RecordValidationToHealthyMs")),
                        "Recovery field types differ")
                if disposition in ("fault", "unknown"):
                    require(recovered["Blocked"] and not recovered["Ready"] and not recovered["Published"] and
                            not recovered["AwaitingReady"] and recovered["Epoch"] == EMPTY_GUID and recovered["Tick"] == 0 and
                            recovered["Reason"] == disposition + "Blocked" and
                            consumer["NativeCounts"]["Created"] == consumer["NativeCounts"]["Reset"] == consumer["NativeCounts"]["Steps"] == 0,
                            "Trusted fault/unknown must block before any native Create")
                else:
                    reason = "ConfirmedWorldLost" if disposition == "lost" else "NormallyReleased"
                    require(not recovered["Blocked"] and recovered["Ready"] and recovered["Published"] and not recovered["AwaitingReady"] and
                            recovered["Tick"] == 1 and guid(recovered["Epoch"]) and recovered["Epoch"] != recovered["PriorEpoch"] and
                            recovered["Reason"] == reason and 0 < recovered["RecordValidationToHealthyMs"] <= 5000 and
                            consumer["NativeCounts"]["Created"] == 1 and consumer["NativeCounts"]["Reset"] == 0 and
                            consumer["NativeCounts"]["Steps"] == 600, "Recovery did not reach bounded new-epoch healthy state and 600 native steps")
                    digest = consumer.get("RawDigest")
                    require(type(digest) is str and re.fullmatch("[A-F0-9]{64}", digest) is not None and
                            consumer.get("RawDigestMeaning") == "complete-native-first-healthy-frame; epoch excluded",
                            "Missing complete first-healthy bit digest")
                    self.healthy_digests.append(digest)
                self.recoveries.append({"Disposition": disposition, "PrefixSteps": prefix, "MetadataBytes": size,
                    "MetadataHash": file_hash(metadata_path), "ProducerPid": producer["ProcessId"], "ConsumerPid": consumer["ProcessId"],
                    "Reason": recovered["Reason"], "Blocked": recovered["Blocked"], "PriorEpoch": recovered["PriorEpoch"],
                    "Epoch": recovered["Epoch"], "RecoveryMilliseconds": recovered["RecordValidationToHealthyMs"], "RawDigest": consumer["RawDigest"]})
        require(len(self.healthy_digests) == 4 and len(set(self.healthy_digests)) == 1,
                "All four Linux first-healthy complete bit digests must be independent of prior history/disposition")

    def run(self):
        try:
            self.profiles()
            suite = self.invoke("suite", ["--mode", "suite"], required=SUITE_CHECKS)
            require(len(COMMON_CHECKS + SUITE_CHECKS) == 43, "Frozen normal checklist must contain 43 checks")
            require(suite["ExternalNativeProbe"] is not None and suite["NativeCounts"]["Created"] > 0 and
                    suite["NativeCounts"]["Reset"] == 0, "Normal suite requires actual native worlds and independent C probe")
            for mutation in MUTATIONS:
                self.invoke("mutant-" + mutation, ["--mode", "suite", "--mutation", mutation], mutation=mutation, passed=False,
                            required=SUITE_CHECKS if mutation == "healthyReset" else ())
            self.cross_process()
            current_hash, current_files = inventory(self.args.source_root, SOURCE_DIRS)
            require(current_hash == self.source_hash and current_files == self.source_files and
                    file_hash(self.args.module) == self.module_hash and file_hash(self.args.probe) == self.probe_hash and
                    file_hash(self.args.tool) == self.tool_hash and Path(self.args.build_record).read_bytes() == self.record_bytes,
                    "Source or executing binaries/build record changed during verification")
            require(all(file_hash(Path(self.args.source_root) / item["Path"]) == item["Sha256"] for item in self.root_files) and
                    all(file_hash(item["Path"]) == item["Sha256"] for item in self.runtime_files),
                    "Root build configuration or executing managed dependencies changed during verification")
            _, evidence_files = inventory(self.evidence)
            write_json(self.evidence / "manifest.json", {"Gate": "isolated-safe-spawn-linux-cross-process", "Pass": True,
                "NativeHash": self.module_hash, "ToolHash": self.tool_hash, "NativeProbeHash": self.probe_hash,
                "NativePlatform": "linux-x64", "BuildRecordHash": self.record_hash, "SourceHash": self.source_hash,
                "ParameterHash": PARAMETER_HASH, "SourceFiles": self.source_files, "RootBuildFiles": self.root_files,
                "ManagedRuntimeFiles": self.runtime_files, "EvidenceFiles": evidence_files,
                "Processes": self.processes, "Recoveries": self.recoveries, "MutationCount": len(MUTATIONS),
                "ProfileRejectionCount": 12, "NormalRequiredCheckCount": 43, "LinuxVerified": True,
                "FormalServerVerified": False, "DatabaseVerified": False, "RealWindowVerified": False,
                "MultiClientVerified": False, "ManualAcceptance": False, "CompleteCheckpointAdmitted": False})
        except Exception as error:
            write_json(self.evidence / "failure.json", {"Gate": "isolated-safe-spawn-linux-cross-process", "Pass": False,
                       "Error": str(error), "Processes": self.processes, "Recoveries": self.recoveries})
            raise


def summarize(args):
    evidence = Path(args.evidence)
    result = load_json(evidence / "run/manifest.json")
    require(result.get("Pass") is True and result.get("LinuxVerified") is True, "Run manifest is not successful")
    source = load_json(evidence / "linux-source-files.json")
    require(result["SourceHash"] == source["SourceHash"] and result["BuildRecordHash"] == file_hash(evidence / "linux-build-record.json"),
            "Root manifest provenance differs")
    box2d = load_json(evidence / "box2d-source-files.json")
    box2d_checks = []
    for stage in ("after-build", "after-run"):
        check = load_json(evidence / ("box2d-check-" + stage + ".json"))
        require(check.get("Pass") is True and check.get("Stage") == stage and check.get("TreeHash") == box2d["TreeHash"] and
                check.get("Commit") == COMMIT and check.get("ArchiveHash") == ARCHIVE_HASH and check.get("LicenseHash") == LICENSE_HASH and
                check.get("FileCount") == len(box2d["Files"]), "Required actual Box2D tree recheck is missing or differs")
        box2d_checks.append(check)
    _, files = inventory(evidence)
    write_json(evidence / "manifest.json", {"Gate": "isolated-safe-spawn-linux-build-and-run", "Pass": True,
        "LinuxVerified": True, "RunManifest": "run/manifest.json", "BuildRecord": "linux-build-record.json",
        "SourceHash": result["SourceHash"], "BuildRecordHash": result["BuildRecordHash"], "EvidenceFiles": files,
        "Box2DTreeHash": box2d["TreeHash"], "Box2DChecks": box2d_checks,
        "FormalServerVerified": False, "DatabaseVerified": False, "RealWindowVerified": False,
        "MultiClientVerified": False, "ManualAcceptance": False, "CompleteCheckpointAdmitted": False})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for name in ("prepare", "record", "verify", "check-box2d", "summarize", "failure"):
        child = commands.add_parser(name)
        child.add_argument("--evidence", required=True)
        if name in ("prepare", "record", "verify"):
            child.add_argument("--source-root", required=True)
        if name == "prepare":
            child.add_argument("--archive", required=True)
            child.add_argument("--workspace", required=True)
        if name in ("record", "verify"):
            child.add_argument("--module", required=True)
            child.add_argument("--probe", required=True)
        if name == "verify":
            child.add_argument("--tool", required=True)
            child.add_argument("--build-record", required=True)
        if name == "failure":
            child.add_argument("--error", required=True)
        if name == "check-box2d":
            child.add_argument("--box2d-root", required=True)
            child.add_argument("--stage", required=True, choices=("after-build", "after-run"))
    args = parser.parse_args()
    try:
        if args.command == "verify":
            require(sys.platform == "linux" and platform.machine() == "x86_64", "Actual verifier requires Linux x86-64")
            Runner(args).run()
        elif args.command == "failure":
            write_json(Path(args.evidence) / "failure.json", {"Gate": "isolated-safe-spawn-linux-build-and-run", "Pass": False, "Error": args.error})
        else:
            globals()[args.command.replace("-", "_")](args)
        return 0
    except Exception as error:
        print(f"Linux gate failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

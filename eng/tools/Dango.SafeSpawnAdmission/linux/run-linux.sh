#!/usr/bin/env bash
# This entry runs only inside the resource-limited, network-disabled P1 container.
set -Eeuo pipefail
input=${1:-/inputs/repo}
archive=${2:-/inputs/box2d.zip}
evidence=${3:-/evidence}
workspace=$(mktemp -d /tmp/dango-p1-linux.XXXXXXXX)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_HOME="$workspace/dotnet-home" NUGET_PACKAGES="$workspace/nuget"
helper="$input/eng/tools/Dango.SafeSpawnAdmission/linux/verify_linux.py"
mkdir -p "$evidence"
on_failure() {
  local code=$?
  trap - ERR
  python3 "$helper" failure --evidence "$evidence" --error "Linux entry failed at line ${BASH_LINENO[0]} (exit $code)" || true
  exit "$code"
}
trap on_failure ERR
python3 "$helper" prepare --source-root "$input" --archive "$archive" --workspace "$workspace" --evidence "$evidence"
repo="$workspace/repo"
box2d="$workspace/box2d/box2d-8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3"
helper="$repo/eng/tools/Dango.SafeSpawnAdmission/linux/verify_linux.py"
cd "$repo"
{
  uname -a
  dotnet --info
  cmake --version
  gcc --version
  python3 --version
  cat /etc/os-release
  for file in /sys/fs/cgroup/cpu.max /sys/fs/cgroup/memory.max /sys/fs/cgroup/pids.max; do
    if [ -f "$file" ]; then printf '%s: ' "$file"; cat "$file"; fi
  done
} > "$evidence/linux-environment.log" 2>&1
exec_command() {
  printf '%q ' "$@" >> "$evidence/build-commands.log"
  printf '\n' >> "$evidence/build-commands.log"
  "$@" >> "$evidence/build.log" 2>&1
}
# PIC is a Linux build requirement for the unchanged Box2D static library.
exec_command cmake -S eng/native/box2d -B "$workspace/module-build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DCMAKE_C_STANDARD_LIBRARIES=-lm -DBOX2D_SOURCE_DIR="$box2d"
exec_command cmake --build "$workspace/module-build" --config Release --target dango_physics_module --parallel 2
exec_command cmake -S eng/tools/Dango.SafeSpawnAdmission/native -B "$workspace/probe-build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DCMAKE_C_STANDARD_LIBRARIES=-lm -DBOX2D_SOURCE_DIR="$box2d"
exec_command cmake --build "$workspace/probe-build" --config Release --target dango_safe_spawn_probe --parallel 2
exec_command dotnet build eng/tools/Dango.SafeSpawnAdmission/Dango.SafeSpawnAdmission.csproj -c Release --ignore-failed-sources -p:NuGetAudit=false
python3 "$helper" check-box2d --box2d-root "$box2d" --stage after-build --evidence "$evidence"
module="$workspace/module-build/dango_physics_module.so"
probe="$workspace/probe-build/dango_safe_spawn_probe"
tool="$repo/eng/tools/Dango.SafeSpawnAdmission/bin/Release/net10.0/Dango.SafeSpawnAdmission.dll"
python3 "$helper" record --source-root "$repo" --module "$module" --probe "$probe" --evidence "$evidence"
python3 "$helper" verify --source-root "$repo" --tool "$tool" --module "$module" --probe "$probe" --build-record "$evidence/linux-build-record.json" --evidence "$evidence/run"
python3 "$helper" check-box2d --box2d-root "$box2d" --stage after-run --evidence "$evidence"
python3 "$helper" summarize --evidence "$evidence"

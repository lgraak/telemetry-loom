#!/usr/bin/env bash
set -euo pipefail

if (($# != 2)); then
    echo "Usage: $0 <archive.tar.gz> <linux-x64|linux-arm64>" >&2
    exit 2
fi

archive="$1"
rid="$2"
[[ -f "${archive}" ]] || { echo "Archive not found: ${archive}" >&2; exit 1; }
[[ "${rid}" == "linux-x64" || "${rid}" == "linux-arm64" ]] || {
    echo "Unsupported RID: ${rid}" >&2
    exit 2
}

mapfile -t entries < <(tar -tzf "${archive}")
((${#entries[@]} > 0)) || { echo "Archive is empty" >&2; exit 1; }
archive_root="${entries[0]%%/*}"
[[ -n "${archive_root}" ]] || { echo "Archive has no top-level directory" >&2; exit 1; }

required=(
    "${archive_root}/app/TelemetryLoom.Service"
    "${archive_root}/app/TelemetryLoom.Service.dll"
    "${archive_root}/app/TelemetryLoom.Service.runtimeconfig.json"
    "${archive_root}/install.sh"
    "${archive_root}/telemetry-loom.service"
    "${archive_root}/INSTALL.md"
    "${archive_root}/LICENSE"
)
for expected in "${required[@]}"; do
    printf '%s\n' "${entries[@]}" | grep -Fxq "${expected}" || {
        echo "Archive is missing: ${expected}" >&2
        exit 1
    }
done

for executable in \
    "${archive_root}/app/TelemetryLoom.Service" \
    "${archive_root}/install.sh"; do
    archived_mode="$(tar -tvzf "${archive}" "${executable}" | awk 'NR == 1 { print $1 }')"
    [[ "${archived_mode}" == "-rwxr-xr-x" ]] || {
        echo "Archive executable has unexpected mode ${archived_mode}: ${executable}" >&2
        exit 1
    }
done

if printf '%s\n' "${entries[@]}" | grep -Eiq \
    '(^|/)(\.git|src|tests|obj|\.vs)/|\.pdb$|appsettings\.Development\.json$|(^|/)config\.json(\.previous)?$|telemetry-loom\.env$'; then
    echo "Archive contains a development artifact or user configuration" >&2
    exit 1
fi

if command -v file >/dev/null; then
    temp_dir="$(mktemp -d)"
    trap 'rm -rf -- "${temp_dir}"' EXIT
    tar -xzf "${archive}" -C "${temp_dir}" "${archive_root}/app/TelemetryLoom.Service"
    binary_description="$(file "${temp_dir}/${archive_root}/app/TelemetryLoom.Service")"
    case "${rid}" in
        linux-x64) grep -Eiq 'x86-64|x86_64' <<<"${binary_description}" ;;
        linux-arm64) grep -Eiq 'aarch64|arm64' <<<"${binary_description}" ;;
    esac || {
        echo "Published executable architecture does not match ${rid}: ${binary_description}" >&2
        exit 1
    }
fi

echo "Validated ${archive} for ${rid}"

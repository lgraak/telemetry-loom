#!/usr/bin/env bash
set -euo pipefail

if (($# < 2 || $# > 3)); then
    echo "Usage: $0 <version> <linux-x64|linux-arm64> [output-directory]" >&2
    exit 2
fi

version="$1"
rid="$2"
output_dir="${3:-dist}"
[[ "${version}" =~ ^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z.-]+)?$ ]] || {
    echo "Version must be numeric dot-separated components with an optional prerelease suffix" >&2
    exit 2
}
[[ "${rid}" == "linux-x64" || "${rid}" == "linux-arm64" ]] || {
    echo "Unsupported RID: ${rid}" >&2
    exit 2
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "${output_dir}"
output_dir="$(cd "${output_dir}" && pwd)"
archive_root="telemetry-loom-${version}-${rid}"
archive_path="${output_dir}/${archive_root}.tar.gz"
work_dir="$(mktemp -d)"
trap 'rm -rf -- "${work_dir}"' EXIT
publish_dir="${work_dir}/${archive_root}/app"
mkdir -p "${publish_dir}"

dotnet publish "${repo_root}/src/TelemetryLoom.Service/TelemetryLoom.Service.csproj" \
    --configuration Release \
    --runtime "${rid}" \
    --self-contained true \
    --output "${publish_dir}" \
    -p:Version="${version}" \
    -p:DebugSymbols=false \
    -p:DebugType=None

rm -f -- "${publish_dir}/appsettings.Development.json"
install -m 0755 "${repo_root}/scripts/install.sh" "${work_dir}/${archive_root}/install.sh"
install -m 0644 "${repo_root}/packaging/telemetry-loom.service" "${work_dir}/${archive_root}/telemetry-loom.service"
install -m 0644 "${repo_root}/docs/installation.md" "${work_dir}/${archive_root}/INSTALL.md"
install -m 0644 "${repo_root}/LICENSE" "${work_dir}/${archive_root}/LICENSE"
chmod 0755 "${publish_dir}/TelemetryLoom.Service"

source_date_epoch="${SOURCE_DATE_EPOCH:-0}"
rm -f -- "${archive_path}"
tar_path="${work_dir}/release.tar"
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="@${source_date_epoch}" \
    --mode='u=rwX,go=rX' -cf "${tar_path}" -C "${work_dir}" "${archive_root}"
tar --delete -f "${tar_path}" \
    "${archive_root}/app/TelemetryLoom.Service" \
    "${archive_root}/install.sh"
tar --owner=0 --group=0 --numeric-owner --mtime="@${source_date_epoch}" --mode=0755 \
    --append -f "${tar_path}" -C "${work_dir}" \
    "${archive_root}/app/TelemetryLoom.Service" \
    "${archive_root}/install.sh"
gzip -n -9 < "${tar_path}" > "${archive_path}"

"${repo_root}/scripts/validate-release-archive.sh" "${archive_path}" "${rid}"
echo "Created ${archive_path}"

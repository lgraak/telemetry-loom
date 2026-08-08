#!/usr/bin/env bash
set -euo pipefail

service_name="telemetry-loom"
service_user="telemetry-loom"
service_group="telemetry-loom"
install_dir="/opt/telemetry-loom"
state_dir="/var/lib/telemetry-loom"
environment_dir="/etc/telemetry-loom"
unit_path="/etc/systemd/system/telemetry-loom.service"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
payload_dir="${script_dir}/app"
unit_source="${script_dir}/telemetry-loom.service"
start_service=false

usage() {
    cat <<'EOF'
Usage: sudo ./install.sh [--start]

Install or upgrade an extracted Telemetry Loom release payload.
  --start  Enable the service at boot and start it after installation.

Without --start, an inactive service remains inactive. An already-running
service is stopped for the payload replacement and started again afterward.
EOF
}

while (($# > 0)); do
    case "$1" in
        --start) start_service=true ;;
        --help|-h) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
    shift
done

if [[ ${EUID} -ne 0 ]]; then
    echo "Run this installer as root, for example: sudo ./install.sh --start" >&2
    exit 1
fi

for command in chmod chown cp cut find getent groupadd install mktemp mv rm rmdir systemctl useradd; do
    command -v "${command}" >/dev/null || {
        echo "Required command not found: ${command}" >&2
        exit 1
    }
done

[[ -x "${payload_dir}/TelemetryLoom.Service" ]] || {
    echo "Release payload is missing executable app/TelemetryLoom.Service" >&2
    exit 1
}
[[ -f "${unit_source}" ]] || {
    echo "Release payload is missing telemetry-loom.service" >&2
    exit 1
}

if ! getent group "${service_group}" >/dev/null; then
    groupadd --system "${service_group}"
fi

if getent passwd "${service_user}" >/dev/null; then
    passwd_entry="$(getent passwd "${service_user}")"
    user_id="$(cut -d: -f3 <<<"${passwd_entry}")"
    primary_group_id="$(cut -d: -f4 <<<"${passwd_entry}")"
    login_shell="$(cut -d: -f7 <<<"${passwd_entry}")"
    expected_group_id="$(getent group "${service_group}" | cut -d: -f3)"
    if [[ "${user_id}" == "0" || "${primary_group_id}" != "${expected_group_id}" ||
          ! "${login_shell}" =~ (nologin|false)$ ]]; then
        echo "Existing ${service_user} account is incompatible; refusing to modify it." >&2
        exit 1
    fi
else
    nologin_shell="$(command -v nologin || true)"
    [[ -n "${nologin_shell}" ]] || nologin_shell="/usr/sbin/nologin"
    useradd --system --gid "${service_group}" --home-dir /nonexistent \
        --no-create-home --shell "${nologin_shell}" "${service_user}"
fi

was_active=false
if systemctl is-active --quiet "${service_name}.service"; then
    was_active=true
    systemctl stop "${service_name}.service"
fi

install -d -o root -g root -m 0755 /opt
install -d -o "${service_user}" -g "${service_group}" -m 0750 "${state_dir}"
install -d -o root -g root -m 0755 "${environment_dir}"

staged_dir="$(mktemp -d /opt/.telemetry-loom.install.XXXXXX)"
backup_dir=""
installation_complete=false
cleanup() {
    if [[ -n "${backup_dir}" && -d "${backup_dir}" && ! -e "${install_dir}" ]]; then
        mv "${backup_dir}" "${install_dir}"
    fi
    [[ ! -d "${staged_dir}" ]] || rm -rf -- "${staged_dir}"
    if [[ "${was_active}" == true && "${installation_complete}" == false ]]; then
        systemctl start "${service_name}.service" || true
    fi
}
trap cleanup EXIT

cp -a "${payload_dir}/." "${staged_dir}/"
chown -R root:root "${staged_dir}"
find "${staged_dir}" -type d -exec chmod 0755 {} +
find "${staged_dir}" -type f -exec chmod 0644 {} +
chmod 0755 "${staged_dir}/TelemetryLoom.Service"

if [[ -d "${install_dir}" ]]; then
    backup_dir="$(mktemp -d /opt/.telemetry-loom.backup.XXXXXX)"
    rmdir "${backup_dir}"
    mv "${install_dir}" "${backup_dir}"
fi

if ! mv "${staged_dir}" "${install_dir}"; then
    [[ -z "${backup_dir}" || -e "${install_dir}" ]] || mv "${backup_dir}" "${install_dir}"
    echo "Application payload replacement failed; the previous payload was restored when available." >&2
    exit 1
fi
staged_dir=""
[[ -z "${backup_dir}" ]] || rm -rf -- "${backup_dir}"

install -o root -g root -m 0644 "${unit_source}" "${unit_path}"
systemctl daemon-reload

if [[ "${start_service}" == true ]]; then
    systemctl enable --now "${service_name}.service"
elif [[ "${was_active}" == true ]]; then
    systemctl start "${service_name}.service"
fi
installation_complete=true

cat <<EOF
Telemetry Loom is installed.

Configuration: ${state_dir}/config.json
Optional overrides: ${environment_dir}/telemetry-loom.env
Browser: http://127.0.0.1:5198

Useful commands:
  systemctl status ${service_name}
  journalctl -u ${service_name} -e
  curl http://127.0.0.1:5198/api/status
EOF

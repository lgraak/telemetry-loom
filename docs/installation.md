# Installation and service operations

Telemetry Loom ships as a self-contained Linux application. Installing a release does not require the .NET SDK or a separately installed ASP.NET Core runtime.

## Supported release targets

Choose the archive matching `uname -m`:

| Machine architecture | Release RID | Archive suffix |
| --- | --- | --- |
| `x86_64` | `linux-x64` | `linux-x64.tar.gz` |
| `aarch64` or `arm64` | `linux-arm64` | `linux-arm64.tar.gz` |

The archives target glibc-based Linux distributions. Alpine and other musl-based systems are not supported by these artifacts. A supported base installation must provide the native libraries used by .NET, including glibc, libgcc, libstdc++, ICU, OpenSSL, Kerberos/GSSAPI, CA certificates, timezone data, and zlib. The distro-specific [.NET dependency lists](https://learn.microsoft.com/dotnet/core/install/linux) are authoritative if the executable reports a missing native library.

The installation helper also requires systemd and ordinary administrative tools such as `getent`, `groupadd`, `useradd`, `install`, `find`, and `tar`.

## Install a release

Download the appropriate archive from [GitHub Releases](https://github.com/lgraak/telemetry-loom/releases), then extract and install it:

```bash
tar -xzf telemetry-loom-<version>-linux-x64.tar.gz
cd telemetry-loom-<version>-linux-x64
sudo ./install.sh --start
```

Replace `linux-x64` with `linux-arm64` on a 64-bit ARM machine. `--start` explicitly enables the unit at boot and starts it. Without that option, a first installation is staged but not enabled or started. Reinstalling over an active service stops it for the application replacement and starts it again.

The helper refuses to run without root privileges. It creates or validates the dedicated `telemetry-loom` system user and group, atomically replaces only the application payload, installs the unit, and reloads systemd. It does not create, overwrite, or delete `config.json`.

Verify the installation:

```bash
systemctl status telemetry-loom
systemctl show telemetry-loom -p User -p Group -p MainPID
curl http://127.0.0.1:5198/api/status
```

Open `http://127.0.0.1:5198` in a browser on the same machine.

## Layout and permissions

| Path | Purpose | Ownership |
| --- | --- | --- |
| `/opt/telemetry-loom` | self-contained application payload | `root:root`, not writable by the service |
| `/var/lib/telemetry-loom` | persistent state | `telemetry-loom:telemetry-loom`, mode `0750` |
| `/var/lib/telemetry-loom/config.json` | active alias/calculation configuration | service-managed |
| `/var/lib/telemetry-loom/config.json.previous` | previous active document retained by atomic replacement | service-managed |
| `/etc/telemetry-loom/telemetry-loom.env` | optional administrator overrides | administrator-managed |
| `/etc/systemd/system/telemetry-loom.service` | installed system unit | `root:root` |

The `telemetry-loom` account is unprivileged, has no interactive login shell, and does not depend on a normal home directory. The systemd unit provides the writable state directory and keeps the application payload read-only to the process.

The packaged service explicitly uses:

```text
TelemetryLoom__ConfigPath=/var/lib/telemetry-loom/config.json
Kestrel__Endpoints__Http__Url=http://127.0.0.1:5198
```

Running Telemetry Loom interactively outside this unit still uses its existing per-user default configuration path.

## Service operations and logs

```bash
sudo systemctl start telemetry-loom
sudo systemctl stop telemetry-loom
sudo systemctl restart telemetry-loom
sudo systemctl enable telemetry-loom
sudo systemctl disable telemetry-loom
systemctl status telemetry-loom
journalctl -u telemetry-loom -e
journalctl -u telemetry-loom -f
```

Standard output and error go to journald. Startup, listener, collector, and configuration errors should therefore appear in `journalctl -u telemetry-loom`.

## Browser, REST, and SSE

The packaged listener is `http://127.0.0.1:5198`. It is deliberately unavailable from other machines.

```bash
curl http://127.0.0.1:5198/api/status
curl http://127.0.0.1:5198/api/sensors
curl http://127.0.0.1:5198/api/aliases
curl http://127.0.0.1:5198/api/calculations
curl -N http://127.0.0.1:5198/api/sensors/stream
```

The browser supports sensor inspection plus alias and calculated-sensor management. REST and SSE contracts are the same as source-run installations.

## Configuration and optional overrides

Use the browser or REST API to change aliases and calculations while the service is running. Configuration writes are atomic. When an active document exists, the preceding contents are retained as `config.json.previous`. A failed write leaves the active file and in-memory configuration unchanged.

If `config.json` is malformed, startup fails and identifies the `.previous` file. Telemetry Loom never restores it silently. Stop the service, inspect both documents, deliberately copy the chosen valid document into place, restore ownership, and start the service:

```bash
sudo systemctl stop telemetry-loom
sudo cp /var/lib/telemetry-loom/config.json.previous /var/lib/telemetry-loom/config.json
sudo chown telemetry-loom:telemetry-loom /var/lib/telemetry-loom/config.json
sudo systemctl start telemetry-loom
```

Optional .NET configuration overrides can be placed in `/etc/telemetry-loom/telemetry-loom.env`, one `NAME=value` per line without `export`. For example:

```text
TelemetryLoom__LiveUpdates__IntervalMilliseconds=2000
```

After editing it, restart the service. Do not change the Kestrel listener to a non-loopback address. Remote administration, authentication, TLS, firewall changes, and reverse proxies are not part of the supported deployment.

## Upgrade or reinstall

Extract the new release and run its installer:

```bash
tar -xzf telemetry-loom-<new-version>-linux-x64.tar.gz
cd telemetry-loom-<new-version>-linux-x64
sudo ./install.sh
systemctl status telemetry-loom
curl http://127.0.0.1:5198/api/status
```

When the unit was active, the installer stops it, replaces `/opt/telemetry-loom`, updates the unit, reloads systemd, and starts it again. `/var/lib/telemetry-loom`, `config.json`, `config.json.previous`, and `/etc/telemetry-loom` are outside the replaced payload and remain untouched. Use `--start` if the service was inactive and should now be enabled and started.

Before an important upgrade, an additional administrator-owned backup is sensible:

```bash
sudo cp -a /var/lib/telemetry-loom /var/lib/telemetry-loom.backup
```

## Uninstall and purge

Normal uninstall removes the service and application but preserves configuration and administrator overrides:

```bash
sudo systemctl disable --now telemetry-loom
sudo rm -f /etc/systemd/system/telemetry-loom.service
sudo systemctl daemon-reload
sudo rm -rf /opt/telemetry-loom
```

`/var/lib/telemetry-loom` and `/etc/telemetry-loom` remain available for a later reinstall.

Purge is destructive and must be explicit. After normal uninstall, inspect the paths, then remove persistent state and the dedicated account only if nothing else uses them:

```bash
sudo rm -rf /var/lib/telemetry-loom /etc/telemetry-loom
sudo userdel telemetry-loom
sudo groupdel telemetry-loom
```

## Build from source

Source builds require the .NET 10 SDK. The commands below were checked against current Microsoft or distro-owned guidance in August 2026. Recheck the linked upstream page when installing on a later distro release.

After installing the SDK:

```bash
git clone https://github.com/lgraak/telemetry-loom.git
cd telemetry-loom
dotnet restore TelemetryLoom.slnx
dotnet build TelemetryLoom.slnx --configuration Release --no-restore
dotnet test TelemetryLoom.slnx --configuration Release --no-build
dotnet run --project src/TelemetryLoom.Service
```

### Debian 12 and 13

Register Microsoft's repository for the matching major release, then install the SDK. This example detects 12 or 13 from `/etc/os-release`:

```bash
. /etc/os-release
wget "https://packages.microsoft.com/config/debian/${VERSION_ID}/packages-microsoft-prod.deb" -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

Upstream: [Install .NET on Debian](https://learn.microsoft.com/dotnet/core/install/linux-debian).

### Ubuntu 22.04, 24.04, and 26.04

Use Ubuntu's supported package feeds. Ubuntu 24.04 and 26.04 provide .NET 10 in the built-in feed. Ubuntu 22.04 uses the Ubuntu .NET backports PPA for .NET 10:

```bash
# Ubuntu 22.04 only
sudo add-apt-repository ppa:dotnet/backports

sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

Do not mix Ubuntu and Microsoft .NET package feeds. Upstream: [Install .NET on Ubuntu](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install).

### Fedora 43 and 44

```bash
sudo dnf install dotnet-sdk-10.0
```

Upstream: [Install .NET on Fedora](https://learn.microsoft.com/dotnet/core/install/linux-fedora).

### RHEL 8, 9, and 10; CentOS Stream 9 and 10

RHEL systems must be registered with Red Hat Subscription Manager. .NET 10 is in the supported AppStream repositories:

```bash
sudo dnf install dotnet-sdk-10.0
```

CentOS Linux is end-of-life and is not the same as CentOS Stream. Upstream: [Install .NET on RHEL and CentOS Stream](https://learn.microsoft.com/dotnet/core/install/linux-rhel).

### Arch Linux, CachyOS, and EndeavourOS

Current Arch `extra` provides the .NET 10 SDK and ASP.NET Core targeting pack. The targeting pack is required to build this web project:

```bash
sudo pacman -Syu dotnet-sdk-10.0 aspnet-targeting-pack-10.0
```

Upstream package records: [dotnet-sdk-10.0](https://archlinux.org/packages/extra/x86_64/dotnet-sdk-10.0/) and [aspnet-targeting-pack-10.0](https://archlinux.org/packages/extra/x86_64/aspnet-targeting-pack-10.0/).

### openSUSE Leap 16

Register Microsoft's Leap 16 repository, then install the SDK:

```bash
sudo zypper install libicu
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc
wget https://packages.microsoft.com/config/opensuse/16/prod.repo
sudo mv prod.repo /etc/zypp/repos.d/microsoft-prod.repo
sudo chown root:root /etc/zypp/repos.d/microsoft-prod.repo
sudo zypper refresh
sudo zypper install dotnet-sdk-10.0
```

Upstream: [Install .NET on openSUSE Leap](https://learn.microsoft.com/dotnet/core/install/linux-opensuse).

## Troubleshooting

### No hwmon sensors

The service always exposes its simulated sensor, so use `/api/sensors` to distinguish service startup from hwmon discovery. Verify the host has readable hwmon entries:

```bash
find /sys/class/hwmon -maxdepth 2 -type f -name '*_input' -readable
sudo -u telemetry-loom find /sys/class/hwmon -maxdepth 2 -type f -name '*_input' -readable
```

Check `journalctl -u telemetry-loom`. The permanent unit deliberately does not grant device capabilities or write access to sysfs. If the service user cannot read an input that ordinary users can read, inspect host permissions or security policy rather than running Telemetry Loom as root. You can also inspect hardening with `systemd-analyze security telemetry-loom.service`.

### Browser or port unavailable

```bash
systemctl status telemetry-loom
journalctl -u telemetry-loom -e
ss -ltnp | grep 5198
curl -v http://127.0.0.1:5198/api/status
```

The expected listener is only `127.0.0.1:5198`. A port conflict appears in journald. Stop the conflicting local service or, for local-only testing, select another loopback port in the environment file. Do not bind to `0.0.0.0`.

### Configuration is not persisted

```bash
systemctl show telemetry-loom -p User -p Group
sudo ls -la /var/lib/telemetry-loom
sudo -u telemetry-loom test -w /var/lib/telemetry-loom
journalctl -u telemetry-loom -e
```

The state directory must be owned by `telemetry-loom:telemetry-loom`. Application files under `/opt` should remain owned by root. Do not make the application directory writable to work around a state-directory problem.

### systemd startup failure

```bash
sudo systemd-analyze verify /etc/systemd/system/telemetry-loom.service
systemctl status telemetry-loom
journalctl -u telemetry-loom -b --no-pager
sudo -u telemetry-loom /opt/telemetry-loom/TelemetryLoom.Service
```

The last command is a foreground diagnostic. Stop the unit first to avoid a port conflict. If a native library is missing, install the package named by your distro's [.NET dependency guidance](https://learn.microsoft.com/dotnet/core/install/linux). If the configuration is malformed, follow the deliberate `.previous` recovery procedure above.

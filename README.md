# Telemetry Loom

Telemetry Loom is a localhost-only Linux service that turns hardware sensor data into stable, typed telemetry for browser users and consumers such as [InfoPanel](https://github.com/lgraak/InfoPanel.TelemetryLoom).

It provides:

- read-only Linux hwmon discovery with stable physical sensor IDs
- presentation enrichment for common CPU, GPU, NVMe, ACPI, and Wi-Fi sensors
- durable aliases and typed calculated sensors
- atomic JSON configuration with retained `.previous` content
- REST APIs and complete-snapshot Server-Sent Events
- responsive browser workflows for inspecting sensors and managing aliases and formulas
- self-contained `linux-x64` and `linux-arm64` release packages with systemd installation

## Install a release

Normal users do not need the .NET SDK or ASP.NET Core runtime. Download the archive for your architecture from [GitHub Releases](https://github.com/lgraak/telemetry-loom/releases), then run:

```bash
tar -xzf telemetry-loom-<version>-linux-x64.tar.gz
cd telemetry-loom-<version>-linux-x64
sudo ./install.sh --start
systemctl status telemetry-loom
```

Use the `linux-arm64` archive on 64-bit ARM systems. Open `http://127.0.0.1:5198` on the installed machine. The service remains bound to localhost.

See [the installation guide](docs/installation.md) for native dependencies, upgrades, logs, configuration, uninstall, and troubleshooting.

## Build from source

Source builds require the .NET 10 SDK:

```bash
dotnet restore TelemetryLoom.slnx
dotnet build TelemetryLoom.slnx --configuration Release --no-restore
dotnet test TelemetryLoom.slnx --configuration Release --no-build
dotnet run --project src/TelemetryLoom.Service
```

Current package-manager paths are:

- Debian 12/13: Microsoft repository, then `sudo apt-get install dotnet-sdk-10.0`
- Ubuntu 22.04/24.04/26.04: supported Ubuntu feed, then `sudo apt-get install dotnet-sdk-10.0`
- Fedora 43/44, RHEL 8–10, CentOS Stream 9/10: `sudo dnf install dotnet-sdk-10.0`
- Arch Linux, CachyOS, EndeavourOS: `sudo pacman -Syu dotnet-sdk-10.0 aspnet-targeting-pack-10.0`
- openSUSE Leap 16: Microsoft repository, then `sudo zypper install dotnet-sdk-10.0`

Exact repository setup and upstream references are in [docs/installation.md](docs/installation.md#build-from-source).

## Interfaces

The browser UI is at `http://127.0.0.1:5198`. Useful endpoints include:

```text
GET /api/status
GET /api/sensors
GET /api/sensors/stream
GET /api/aliases
GET /api/calculations
```

Public behavior is documented in:

- [aliases](docs/aliases.md)
- [calculated sensors](docs/calculated-sensors.md) and [formula semantics](docs/formula-semantics.md)
- [live updates](docs/live-updates.md)
- [sensor glossary](docs/sensor-glossary.md)
- [design decisions](docs/design-decisions.md)

## Project status

Milestones 1 through 7 are complete. Milestone 8 adds self-contained Linux release packaging, systemd deployment, lifecycle documentation, and installation validation. NVML, history, alerts, authentication, TLS, and remote administration remain outside the current scope.

Telemetry Loom is licensed under GPL-3.0. See [LICENSE](LICENSE).

# Telemetry Loom handoff

Updated: 2026-08-02

## Product direction

Telemetry Loom is a local-first Linux telemetry intermediary, not an HWiNFO replacement. It collects disparate sensor sources, gives them stable identities and user aliases, supports derived sensors with unit-aware formulas, and exposes normalized readings to InfoPanel and other local consumers.

Temperature and other units are structured metadata. Collectors preserve a canonical native unit, formulas operate on compatible quantities, and display-unit conversion belongs at the calculated-sensor or consumer boundary. Initial formula support can require matching units; configurable Celsius/Fahrenheit and other conversions are deliberately incremental.

## Repository and runtime

- Repository: `Lgraak/telemetry-loom` on GitHub, currently private
- Application, executable, and service name: `telemetry-loom` / `telemetry-loom.service`
- Implementation: C# on .NET 10; specialized native collectors may be added later if justified
- Linux runtime target, with Windows-supported development and tests
- Localhost-only HTTP service by default
- Arch/CachyOS requires `aspnet-targeting-pack-10.0` and `aspnet-runtime-10.0` in addition to `dotnet-sdk-10.0`; the SDK package alone can build the collector but not the ASP.NET service

## Milestone status

1. Repository foundation and simulated sensor: complete
2. Linux hwmon discovery and stable identity: complete and merged in PR #1
3. Aliases and JSON configuration persistence: complete on branch `codex/milestone-3-aliases`; awaiting PR merge
4. Unit-aware arithmetic formulas
5. Live API updates
6. InfoPanel plugin
7. Browser configuration interface
8. Installation documentation and systemd packaging

## Milestone 2 validation

A real CachyOS capture found 6 hwmon devices, 21 relevant raw attributes, and 12 exposed readings:

- ACPI temperature
- three NVMe temperatures
- AMD GPU clock, two voltage channels, input and average power, and edge temperature
- AMD CPU `Tctl` temperature
- Intel Wi-Fi temperature

The Logitech HID++ battery device is discovered but has no supported hwmon measurement attributes. Battery capacity should be handled by a future Linux power-supply collector rather than folded into hwmon.

Live SSH validation found and corrected a nested-symlink resolution bug in the first capture. The canonical hardware paths now resolve through `/sys/class/hwmon/hwmonN` and its `device` link before normalization. Normalized paths intentionally omit the `/sys/devices/` prefix and temporary `hwmonN` components. Stable IDs use driver, normalized hardware path, sensor type, channel, and measurement. Labels remain display metadata and do not affect identity.

The sanitized regression fixture is `tests/TelemetryLoom.Core.Tests/TestData/cachyos-amd.json`.

After installing the Arch ASP.NET Core targeting and runtime packs, the complete solution restored and all 18 tests passed on the CachyOS host. A production-mode localhost smoke test returned `available` from `/api/status`, reporting 13 sensors and the `hwmon` and `simulated` collectors. `/api/sensors` returned 12 live hwmon readings plus the simulated sensor with canonical stable IDs and normalized units. The temporary test service was stopped afterward.

## Milestone 3 design

- Alias keys are user-owned lowercase identifiers such as `cooling.air.intake`; display names are separate and freely renameable.
- Collectors do not assign aliases. Aliases bind one-to-one to stable sensor IDs.
- Version 1 JSON persistence snapshots quantity, unit, symbol, and source so missing hardware remains represented as an unavailable sensor.
- Writes flush a same-directory temporary file and atomically replace the active configuration before updating in-memory state.
- User services default to the XDG configuration directory. A future system package can override `TelemetryLoom__ConfigPath` to `/var/lib/telemetry-loom/config.json`.
- The localhost API supports alias list, lookup, upsert, delete, and sensor lookup by alias.
- Formulas and unit conversion remain Milestone 4 work.

## Milestone 3 validation

- 28 tests pass on Windows and CachyOS, including HTTP API integration, JSON restart persistence, missing-hardware preservation, rename-while-missing behavior, invalid configuration rejection, and persistence-failure rollback.
- A zero-warning Release build passes on Windows.
- Live CachyOS validation created `cooling.air.intake` against the ACPI temperature sensor, resolved the aliased `20 °C` reading through `/api/sensors/by-alias/cooling.air.intake`, and verified the persisted schema-versioned JSON after restarting the service.
- Both temporary service instances were stopped and all explicitly named validation files were removed.

## Immediate next decision

Merge the Milestone 3 PR after CI passes, then define Milestone 4's formula document and unit-compatibility rules. The first formula slice should support alias operands and basic arithmetic while requiring compatible canonical units.

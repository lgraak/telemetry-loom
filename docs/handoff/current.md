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

## Milestone status

1. Repository foundation and simulated sensor: complete
2. Linux hwmon discovery and stable identity: complete on branch `codex/milestone-2-hwmon`
3. Aliases and JSON configuration persistence: next
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

## Immediate next decision

Merge Milestone 2 after CI passes, then design Milestone 3 around durable user aliases stored in a versioned JSON configuration file. Aliases must bind to stable sensor IDs while retaining enough metadata to diagnose missing or changed hardware.

# Contributing

Telemetry Loom is early-stage. Before implementing a large feature, open an issue describing the practical problem and the smallest useful change.

## Development

1. Install the .NET 10 SDK.
2. Run `dotnet restore TelemetryLoom.slnx`.
3. Run `dotnet format TelemetryLoom.slnx --verify-no-changes --no-restore`.
4. Run `dotnet build TelemetryLoom.slnx --configuration Release --no-restore`.
5. Run `dotnet test TelemetryLoom.slnx --configuration Release --no-build`.

## Hardware fixtures

Collectors must support fixture-based testing so contributors do not need identical hardware. Review captures before sharing or committing them. Keep only attributes required to reproduce behavior, and do not add serial numbers, firmware data, controls, or unrelated sysfs content. Do not substitute zero when a sensor is unavailable.

See [docs/hwmon-fixtures.md](docs/hwmon-fixtures.md) for the hwmon capture boundary.

## Pull requests

Keep changes focused and include tests for behavior that can be verified without specific hardware. Explain the practical problem, user impact, validation performed, and anything deliberately deferred. Update durable public documentation when supported behavior or architecture changes; keep transient branch and handoff state out of the public repository.

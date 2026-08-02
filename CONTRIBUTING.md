# Contributing

Telemetry Loom is early-stage. Before implementing a large feature, open an issue describing the practical problem and the smallest useful change.

## Development

1. Install the .NET 10 SDK.
2. Run `dotnet restore TelemetryLoom.slnx`.
3. Run `dotnet test TelemetryLoom.slnx` before submitting a pull request.
4. Keep changes focused and include tests where behavior can be verified without specific hardware.

Collectors must support fixture-based testing so contributors do not need identical hardware. Do not substitute zero when a sensor is unavailable.

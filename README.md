# Telemetry Loom

Telemetry Loom is a small local Linux service that normalizes hardware sensor data, assigns durable aliases, and produces calculated sensors for consumers such as InfoPanel.

The project is intentionally not an attempt to recreate HWiNFO. Its job is to sit between disparate sensor sources and applications that need stable, understandable values.

## Current status

Milestone 2 provides the repository foundation and initial Linux hwmon discovery:

- a structured sensor model with quantity, unit, availability, and timestamp metadata
- a simulated Celsius temperature sensor with the stable alias `demo.temperature`
- read-only discovery of hwmon temperature, fan, voltage, current, power, frequency, and humidity channels
- stable hwmon IDs based on the driver, normalized hardware path, sensor type, channel, and measurement
- fixture capture and replay without requiring contributor hardware
- a localhost HTTP API
- a minimal browser status page
- automated unit tests and GitHub Actions CI

NVML, aliases, formulas, persistence, and the InfoPanel plugin are not implemented yet.

## Architecture direction

```text
hwmon / NVML / future collectors
              |
              v
normalized sensors -> aliases -> calculated sensors -> local HTTP API
                                                       |-> InfoPanel plugin
                                                       |-> browser configuration
                                                       `-> future local consumers
```

Units are structured data rather than arbitrary display strings. Initial calculations will require matching units. User-selectable conversion is deliberately deferred, but the core model distinguishes absolute temperatures from temperature differences so it can be added safely.

## Requirements

- .NET 10 SDK for the complete solution
- Linux is the runtime target; development and tests also work on Windows

## Run the current milestone

```bash
dotnet restore TelemetryLoom.slnx
dotnet run --project src/TelemetryLoom.Service
```

Open `http://127.0.0.1:5198` or query:

```bash
curl http://127.0.0.1:5198/api/sensors
curl http://127.0.0.1:5198/api/status
```

Override the local listen address with ASP.NET Core Kestrel configuration, for example:

```bash
Kestrel__Endpoints__Http__Url=http://127.0.0.1:5200 dotnet run --project src/TelemetryLoom.Service
```

The service must remain localhost-only by default. Remote access and authentication are outside the initial scope.

## Test

```bash
dotnet test TelemetryLoom.slnx
```

## Capture an hwmon fixture

On the Linux machine whose sensors you want to test:

```bash
dotnet run --project src/TelemetryLoom.Hwmon.Capture -- hwmon-fixture.json
```

The command reads only standard hwmon identity, input, average, label, and fault attributes. Review the JSON before sharing it. See [docs/hwmon-fixtures.md](docs/hwmon-fixtures.md) for the captured fields and current limitations.

## Planned milestones

1. Repository foundation and simulated sensor (complete)
2. hwmon discovery and stable sensor identity (implemented; awaiting real-hardware fixture validation)
3. aliases and JSON configuration persistence
4. unit-aware arithmetic formulas
5. live API updates
6. InfoPanel plugin
7. browser configuration interface
8. installation documentation and systemd packaging

## License

Telemetry Loom is licensed under GPL-3.0. See [LICENSE](LICENSE).

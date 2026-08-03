# Contributing sensor enrichment

Sensor enrichment is presentation metadata, not sensor discovery. Do not change a stable ID, normalized value, quantity, unit, status, or collector behavior to improve a label.

## Workflow

1. Capture a fixture on the target Linux system with `TelemetryLoom.Hwmon.Capture`.
2. Inspect the JSON before sharing it. Remove anything outside the capture schema and do not add serial numbers, firmware data, control attributes, or unrelated sysfs content.
3. Identify the sensor using structured fields: source, driver, raw label, quantity, measurement, device key, and optional device label. Never parse a stable sensor ID to recover these fields.
4. Find an authoritative upstream source for the interpretation. Kernel documentation, kernel source, or a vendor specification is preferred.
5. Add the smallest applicable rule to `SensorPresentationRegistry`.
6. Assign confidence honestly. Use `Generic` when the component behind a channel is vendor-defined or ambiguous.
7. Add the documentation key to [the glossary](sensor-glossary.md) and cite the source.
8. Add fixture-backed tests for the new rule and a safe fallback test for related unknown labels.

Device grouping must use `DeviceGroupKey`. A user-visible device name can repeat; the group key must remain stable and distinguish multiple devices. Presentation logic may use collector metadata but must not reinterpret or rewrite the sensor ID.

## Required checks

Run the complete solution validation:

```bash
dotnet restore TelemetryLoom.slnx
dotnet format TelemetryLoom.slnx --verify-no-changes --no-restore
dotnet build TelemetryLoom.slnx --configuration Release --no-restore
dotnet test TelemetryLoom.slnx --configuration Release --no-build
```

For a new platform fixture, also inspect the resulting API response and confirm that unknown drivers remain usable with `Generic` confidence rather than receiving a guessed hardware meaning.


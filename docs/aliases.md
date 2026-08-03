# Sensor aliases

Aliases are user-owned, durable names bound to collector sensor IDs. Collectors never assign aliases. A display name can change without changing the alias key used by APIs or future formulas.

Recommended keys use descriptive lowercase segments:

```text
cooling.air.intake
cooling.air.exhaust
cpu.package.temperature
```

Keys must start with a letter and contain no more than 128 lowercase letters, digits, `.`, `_`, or `-`. Separators must occur between alphanumeric segments. Each key is unique and each sensor can have at most one configured alias.

## API

Create or replace an alias for a currently visible sensor:

```bash
curl -X PUT http://127.0.0.1:5198/api/aliases/cooling.air.intake \
  -H 'Content-Type: application/json' \
  -d '{
    "displayName": "Radiator Intake Air",
    "sensorId": "hwmon:acpitz-0:43c1381b14be5fc2:temp:1:input"
  }'
```

List and resolve aliases:

```bash
curl http://127.0.0.1:5198/api/aliases
curl http://127.0.0.1:5198/api/aliases/cooling.air.intake
curl http://127.0.0.1:5198/api/sensors/by-alias/cooling.air.intake
```

Delete an alias:

```bash
curl -X DELETE http://127.0.0.1:5198/api/aliases/cooling.air.intake
```

Creating an alias or rebinding one to a different sensor requires the target sensor to be visible. An existing alias can still be renamed while its bound hardware is missing.

## Persistence

Configuration uses a versioned JSON document. Writes create, flush, and validate a temporary file in the same directory before atomically replacing the active file. When an active file already exists, its prior contents are retained as `config.json.previous`. A failed write does not replace the active file or the registry's in-memory state.

The default user-service locations are:

- Linux: `$XDG_CONFIG_HOME/telemetry-loom/config.json`, or `~/.config/telemetry-loom/config.json`
- Windows development: `%LOCALAPPDATA%\TelemetryLoom\config.json`

Override the path with the .NET configuration key `TelemetryLoom:ConfigPath`. Environment variables use double underscores:

```bash
TelemetryLoom__ConfigPath=/var/lib/telemetry-loom/config.json telemetry-loom
```

The persisted record snapshots quantity, stable unit code, and source metadata. The display symbol is derived from the canonical `UnitCatalog` and is not persisted. If hardware disappears, its configured alias remains in `/api/sensors` with an `Unavailable` status and no value. This preserves references for future formulas and consumers until the device returns.

The service does not silently load `config.json.previous` when the active configuration is malformed. Startup fails with an error that identifies the previous file. Recovery is deliberate: stop the service, inspect both files, replace `config.json` with the chosen valid version, and restart.

Prefer the API for edits while the service is running. The file is loaded when the alias registry starts; direct file edits require a service restart.

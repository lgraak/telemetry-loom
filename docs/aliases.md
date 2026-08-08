# Sensor aliases

Aliases are user-owned, durable names bound to collector sensor IDs. Collectors never assign aliases. A display name can change without changing the alias key used by APIs or future formulas.

Recommended keys use descriptive lowercase segments:

```text
cooling.air.intake
cooling.air.exhaust
cpu.package.temperature
```

Keys must start with a letter and contain no more than 128 lowercase letters, digits, `.`, or `_`. Separators must occur between alphanumeric segments. Hyphens are excluded because `-` is the formula subtraction operator. Each key is unique across physical aliases and calculated sensors, and each physical sensor can have at most one configured alias.

The one-alias-per-sensor rule is deliberate for the initial product. It keeps reverse lookup, sensor decoration, the configuration interface, and diagnostics unambiguous. Rename a display name without changing its alias key. Multiple compatibility aliases remain deferred until a concrete consumer requires them.

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

Alias writes require a loopback/local connection. When the service is explicitly configured for LAN read-only access, remote clients may list and resolve aliases but create, replace, and delete requests return HTTP 403.

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

Prefer the API for edits while the service is running. The file is loaded when the configuration registries start; direct file edits require a service restart.

See [examples/config.json](examples/config.json) for the canonical pre-release configuration shape. A formal JSON Schema is deferred while the version 1 document is still changing before release; the typed model and validation tests remain authoritative for now.
